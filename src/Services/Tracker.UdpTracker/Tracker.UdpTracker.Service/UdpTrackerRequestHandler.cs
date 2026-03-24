using System.Buffers.Binary;
using System.Net;
using Microsoft.Extensions.Options;
using BeeTracker.BuildingBlocks.Observability.Diagnostics;
using Tracker.Gateway.Application.Announce;
using Tracker.UdpTracker.Protocol;

namespace Tracker.UdpTracker.Service;

public sealed class UdpTrackerRequestHandler
{
    private readonly ConnectionIdManager _connectionIdManager;
    private readonly IRuntimeSwarmStore _runtimeSwarmStore;
    private readonly IAccessPolicyResolver _accessPolicyResolver;
    private readonly IRuntimeGovernanceState _governanceState;
    private readonly GatewayRuntimeOptions _runtimeOptions;
    private readonly UdpTrackerOptions _udpOptions;

    public UdpTrackerRequestHandler(
        ConnectionIdManager connectionIdManager,
        IRuntimeSwarmStore runtimeSwarmStore,
        IAccessPolicyResolver accessPolicyResolver,
        IRuntimeGovernanceState governanceState,
        IOptions<GatewayRuntimeOptions> runtimeOptions,
        IOptions<UdpTrackerOptions> udpOptions)
    {
        _connectionIdManager = connectionIdManager;
        _runtimeSwarmStore = runtimeSwarmStore;
        _accessPolicyResolver = accessPolicyResolver;
        _governanceState = governanceState;
        _runtimeOptions = runtimeOptions.Value;
        _udpOptions = udpOptions.Value;
    }

    public async ValueTask<int> HandleAsync(byte[] datagram, int datagramLength, IPEndPoint remoteEndpoint, byte[] responseBuffer, CancellationToken cancellationToken)
    {
        if (!UdpProtocolParser.TryParseAction(datagram.AsSpan(0, datagramLength), out var action, out var transactionId))
        {
            return 0;
        }

        // ── UDP governance check ──
        if (_governanceState.UdpDisabled)
        {
            TrackerDiagnostics.GovernanceUdpRejected.Add(1);
            return WriteError(responseBuffer, transactionId, "UDP tracker is temporarily disabled");
        }

        if (_governanceState.GlobalMaintenanceMode)
        {
            TrackerDiagnostics.GovernanceMaintenanceRejected.Add(1);
            return WriteError(responseBuffer, transactionId, "tracker is in maintenance mode");
        }

        try
        {
            return action switch
            {
                UdpAction.Connect => HandleConnect(datagram, datagramLength, responseBuffer),
                UdpAction.Announce => await HandleAnnounceAsync(datagram, datagramLength, remoteEndpoint, responseBuffer, cancellationToken),
                UdpAction.Scrape => HandleScrape(datagram, datagramLength, responseBuffer),
                _ => WriteError(responseBuffer, transactionId, "invalid action")
            };
        }
        catch
        {
            TrackerDiagnostics.UdpErrorTotal.Add(1);
            return WriteError(responseBuffer, transactionId, "internal error");
        }
    }

    private int HandleConnect(byte[] data, int length, byte[] responseBuffer)
    {
        if (!UdpProtocolParser.TryParseConnectRequest(data.AsSpan(0, length), out var request))
        {
            TrackerDiagnostics.UdpErrorTotal.Add(1);
            return 0;
        }

        var connectionId = _connectionIdManager.Issue();
        TrackerDiagnostics.UdpConnectTotal.Add(1);

        return UdpProtocolSerializer.WriteConnectResponse(responseBuffer,
            new UdpConnectResponse(request.TransactionId, connectionId));
    }

    private async ValueTask<int> HandleAnnounceAsync(byte[] data, int length, IPEndPoint remoteEndpoint, byte[] responseBuffer, CancellationToken cancellationToken)
    {
        if (!UdpProtocolParser.TryParseAnnounceRequest(data.AsSpan(0, length), out var udpRequest))
        {
            TrackerDiagnostics.UdpErrorTotal.Add(1);
            return WriteError(responseBuffer, 0, "invalid announce request");
        }

        if (!_connectionIdManager.Validate(udpRequest.ConnectionId))
        {
            TrackerDiagnostics.UdpErrorTotal.Add(1);
            return WriteError(responseBuffer, udpRequest.TransactionId, "invalid connection id");
        }

        if (udpRequest.Port == 0)
        {
            TrackerDiagnostics.UdpErrorTotal.Add(1);
            return WriteError(responseBuffer, udpRequest.TransactionId, "invalid port");
        }

        var ipAddress = udpRequest.IpAddress != 0
            ? udpRequest.IpAddress
            : GetIpv4NetworkOrder(remoteEndpoint.Address);

        var endpoint = PeerEndpoint.FromIPv4(ipAddress, udpRequest.Port);
        var infoHash = InfoHashKey.FromBytes(udpRequest.InfoHash);
        var peerId = PeerIdKey.FromBytes(udpRequest.PeerId);
        var trackerEvent = UdpProtocolParser.MapUdpEvent(udpRequest.Event);

        var numWant = udpRequest.NumWant <= 0 ? 50 : Math.Min(udpRequest.NumWant, _runtimeOptions.MaxPeersPerResponse);
        var announceRequest = new AnnounceRequest(
            infoHash, peerId, endpoint,
            udpRequest.Uploaded, udpRequest.Downloaded, udpRequest.Left,
            numWant,
            true, false, trackerEvent, null, null, null);

        if (_governanceState.AnnounceDisabled)
        {
            TrackerDiagnostics.GovernanceAnnounceRejected.Add(1);
            return WriteError(responseBuffer, udpRequest.TransactionId, "announce is temporarily disabled");
        }

        var accessResolution = await _accessPolicyResolver.ResolveAsync(announceRequest, cancellationToken);
        if (!accessResolution.IsAllowed)
        {
            TrackerDiagnostics.UdpErrorTotal.Add(1);
            return WriteError(responseBuffer, udpRequest.TransactionId, accessResolution.FailureReason);
        }

        // Per-torrent UDP check
        if (!accessResolution.Policy.AllowUdp)
        {
            TrackerDiagnostics.UdpErrorTotal.Add(1);
            return WriteError(responseBuffer, udpRequest.TransactionId, "UDP is not allowed for this torrent");
        }

        // Per-torrent governance checks
        if (accessResolution.Policy.MaintenanceFlag)
        {
            TrackerDiagnostics.TorrentMaintenanceRejected.Add(1);
            return WriteError(responseBuffer, udpRequest.TransactionId, "torrent is under maintenance");
        }

        if (accessResolution.Policy.TemporaryRestriction)
        {
            TrackerDiagnostics.TorrentTemporaryRestrictionRejected.Add(1);
            return WriteError(responseBuffer, udpRequest.TransactionId, "torrent is temporarily restricted");
        }

        var now = DateTimeOffset.UtcNow;

        // Read-only mode: skip mutation
        SwarmCounts counts;
        if (_governanceState.ReadOnlyMode)
        {
            TrackerDiagnostics.GovernanceReadOnlySkipped.Add(1);
            counts = _runtimeSwarmStore.GetCounts(infoHash, now);
        }
        else
        {
            var peerTtl = TimeSpan.FromSeconds(accessResolution.Policy.AnnounceIntervalSeconds * 1.5);
            counts = _runtimeSwarmStore.ApplyMutation(announceRequest, peerTtl, now);
        }

        var maxPeersInResponse = Math.Min(
            (int)(((uint)_udpOptions.MaxDatagramSize - 20) / 6),
            _runtimeOptions.MaxPeersPerResponse);
        var requestedPeers = Math.Min(announceRequest.RequestedPeers, maxPeersInResponse);

        using var selection = trackerEvent == TrackerEvent.Stopped
            ? default
            : _runtimeSwarmStore.SelectPeers(announceRequest, requestedPeers, now);

        var peerData = BuildCompactV4PeerData(selection.Peers);

        TrackerDiagnostics.UdpAnnounceTotal.Add(1);

        return UdpProtocolSerializer.WriteAnnounceResponse(responseBuffer,
            new UdpAnnounceResponse(udpRequest.TransactionId, accessResolution.Policy.AnnounceIntervalSeconds,
                counts.LeecherCount, counts.SeederCount, peerData));
    }

    private int HandleScrape(byte[] data, int length, byte[] responseBuffer)
    {
        if (_governanceState.ScrapeDisabled)
        {
            TrackerDiagnostics.GovernanceScrapeRejected.Add(1);
            return WriteError(responseBuffer, 0, "scrape is temporarily disabled");
        }

        if (!UdpProtocolParser.TryParseScrapeRequest(data.AsSpan(0, length), out var request))
        {
            TrackerDiagnostics.UdpErrorTotal.Add(1);
            return WriteError(responseBuffer, 0, "invalid scrape request");
        }

        if (!_connectionIdManager.Validate(request.ConnectionId))
        {
            TrackerDiagnostics.UdpErrorTotal.Add(1);
            return WriteError(responseBuffer, request.TransactionId, "invalid connection id");
        }

        var maxHashes = (_udpOptions.MaxDatagramSize - 8) / UdpProtocolConstants.ScrapeEntrySize;
        var hashCount = Math.Min(request.InfoHashes.Length, maxHashes);
        var entries = new UdpScrapeEntry[hashCount];
        var now = DateTimeOffset.UtcNow;

        for (var i = 0; i < hashCount; i++)
        {
            var infoHash = InfoHashKey.FromBytes(request.InfoHashes[i]);
            var counts = _runtimeSwarmStore.GetCounts(infoHash, now);
            entries[i] = new UdpScrapeEntry(counts.SeederCount, counts.DownloadedCount, counts.LeecherCount);
        }

        TrackerDiagnostics.UdpScrapeTotal.Add(1);

        return UdpProtocolSerializer.WriteScrapeResponse(responseBuffer,
            new UdpScrapeResponse(request.TransactionId, entries));
    }

    private static int WriteError(byte[] buffer, int transactionId, string message)
    {
        return UdpProtocolSerializer.WriteErrorResponse(buffer, new UdpErrorResponse(transactionId, message));
    }

    private static byte[] BuildCompactV4PeerData(PeerSelectionResult selection)
    {
        var span = selection.AsSpan();
        var result = new byte[span.Length * 6];
        for (var i = 0; i < span.Length; i++)
        {
            span[i].Endpoint.WriteCompactBytes(result.AsSpan(i * 6, 6));
        }

        return result;
    }

    private static uint GetIpv4NetworkOrder(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        Span<byte> bytes = stackalloc byte[4];
        address.TryWriteBytes(bytes, out _);
        return BinaryPrimitives.ReadUInt32BigEndian(bytes);
    }
}
