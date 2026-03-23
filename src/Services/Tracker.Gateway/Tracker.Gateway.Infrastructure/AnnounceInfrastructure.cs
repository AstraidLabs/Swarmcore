using System.Buffers;
using System.Buffers.Binary;
using System.Globalization;
using System.Net;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Swarmcore.BuildingBlocks.Abstractions.Hosting;
using Swarmcore.BuildingBlocks.Abstractions.Options;
using Swarmcore.BuildingBlocks.Abstractions.Time;
using Swarmcore.BuildingBlocks.Observability.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Swarmcore.Contracts.Configuration;
using Swarmcore.Hosting;
using Swarmcore.Persistence.Postgres;
using Swarmcore.Serialization.BEncoding;
using Tracker.Gateway.Application.Announce;

namespace Tracker.Gateway.Infrastructure;

public sealed class AnnounceRequestParser(IOptions<TrackerSecurityOptions> securityOptions) : IAnnounceRequestParser
{
    public bool TryParse(HttpContext httpContext, string? passkey, out AnnounceRequest request, out AnnounceError error)
    {
        request = default;
        error = default;

        if (!TrackerRequestParsing.TryGetRemoteEndpoint(httpContext, securityOptions.Value.AllowIPv6Peers, out var endpoint))
        {
            error = new AnnounceError(StatusCodes.Status400BadRequest, "unsupported remote address");
            return false;
        }

        var rawQuery = httpContext.Request.QueryString.Value;
        if (string.IsNullOrEmpty(rawQuery))
        {
            error = new AnnounceError(StatusCodes.Status400BadRequest, "missing query string");
            return false;
        }

        Span<byte> infoHashBytes = stackalloc byte[20];
        Span<byte> peerIdBytes = stackalloc byte[20];
        var uploaded = 0L;
        var downloaded = 0L;
        var left = 0L;
        var numWant = 50;
        var compact = true;
        var trackerEvent = TrackerEvent.None;
        var infoHashFound = false;
        var peerIdFound = false;
        var portOverride = endpoint.Port;
        var queryPasskey = passkey;

        var query = rawQuery.AsSpan();
        if (query.Length > 0 && query[0] == '?')
        {
            query = query[1..];
        }

        while (!query.IsEmpty)
        {
            var ampersand = query.IndexOf('&');
            var segment = ampersand >= 0 ? query[..ampersand] : query;
            query = ampersand >= 0 ? query[(ampersand + 1)..] : [];

            var equals = segment.IndexOf('=');
            if (equals <= 0)
            {
                continue;
            }

            var key = segment[..equals];
            var value = segment[(equals + 1)..];

            if (key.SequenceEqual("info_hash"))
            {
                infoHashFound = TrackerRequestParsing.TryDecodeExact20Bytes(value, infoHashBytes);
            }
            else if (key.SequenceEqual("peer_id"))
            {
                peerIdFound = TrackerRequestParsing.TryDecodeExact20Bytes(value, peerIdBytes);
            }
            else if (key.SequenceEqual("port"))
            {
                if (!ushort.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out portOverride))
                {
                    error = new AnnounceError(StatusCodes.Status400BadRequest, "invalid port");
                    return false;
                }
            }
            else if (key.SequenceEqual("uploaded"))
            {
                if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out uploaded))
                {
                    error = new AnnounceError(StatusCodes.Status400BadRequest, "invalid uploaded");
                    return false;
                }
            }
            else if (key.SequenceEqual("downloaded"))
            {
                if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out downloaded))
                {
                    error = new AnnounceError(StatusCodes.Status400BadRequest, "invalid downloaded");
                    return false;
                }
            }
            else if (key.SequenceEqual("left"))
            {
                if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out left))
                {
                    error = new AnnounceError(StatusCodes.Status400BadRequest, "invalid left");
                    return false;
                }
            }
            else if (key.SequenceEqual("numwant"))
            {
                if (!int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out numWant))
                {
                    error = new AnnounceError(StatusCodes.Status400BadRequest, "invalid numwant");
                    return false;
                }
            }
            else if (key.SequenceEqual("compact"))
            {
                compact = !value.SequenceEqual("0");
            }
            else if (key.SequenceEqual("event"))
            {
                trackerEvent = TrackerRequestParsing.ParseEvent(value);
            }
            else if (key.SequenceEqual("passkey") && string.IsNullOrWhiteSpace(queryPasskey))
            {
                queryPasskey = value.ToString();
            }
        }

        if (!infoHashFound)
        {
            error = new AnnounceError(StatusCodes.Status400BadRequest, "missing info_hash");
            return false;
        }

        if (!peerIdFound)
        {
            error = new AnnounceError(StatusCodes.Status400BadRequest, "missing peer_id");
            return false;
        }

        if (portOverride == 0)
        {
            error = new AnnounceError(StatusCodes.Status400BadRequest, "invalid port");
            return false;
        }

        request = new AnnounceRequest(
            InfoHashKey.FromBytes(infoHashBytes),
            PeerIdKey.FromBytes(peerIdBytes),
            new PeerEndpoint(
                endpoint.AddressFamily,
                endpoint.AddressPart0NetworkOrder,
                endpoint.AddressPart1NetworkOrder,
                endpoint.AddressPart2NetworkOrder,
                endpoint.AddressPart3NetworkOrder,
                portOverride),
            uploaded,
            downloaded,
            left,
            numWant,
            compact,
            trackerEvent,
            queryPasskey);

        return true;
    }
}

public sealed class AnnounceRequestValidator(IOptions<TrackerSecurityOptions> securityOptions) : IAnnounceRequestValidator
{
    public AnnounceValidationResult Validate(in AnnounceRequest request)
    {
        if (request.Endpoint.IsEmpty)
        {
            return AnnounceValidationResult.Fail(StatusCodes.Status400BadRequest, "invalid endpoint");
        }

        if (request.Uploaded < 0 || request.Downloaded < 0 || request.Left < 0)
        {
            return AnnounceValidationResult.Fail(StatusCodes.Status400BadRequest, "negative counters are not allowed");
        }

        if (request.RequestedPeers < -1)
        {
            return AnnounceValidationResult.Fail(StatusCodes.Status400BadRequest, "invalid numwant");
        }

        if (request.RequestedPeers > securityOptions.Value.HardMaxNumWant)
        {
            return AnnounceValidationResult.Fail(StatusCodes.Status400BadRequest, "numwant exceeds hard limit");
        }

        if (securityOptions.Value.RequireCompactResponses && !request.Compact)
        {
            return AnnounceValidationResult.Fail(StatusCodes.Status400BadRequest, "only compact responses are supported");
        }

        return AnnounceValidationResult.Success();
    }
}

public sealed class ScrapeRequestParser(IOptions<TrackerSecurityOptions> securityOptions) : IScrapeRequestParser
{
    public bool TryParse(HttpContext httpContext, string? passkey, out ScrapeRequest request, out AnnounceError error)
    {
        request = default;
        error = default;

        if (!TrackerRequestParsing.TryGetRemoteEndpoint(httpContext, securityOptions.Value.AllowIPv6Peers, out _))
        {
            error = new AnnounceError(StatusCodes.Status400BadRequest, "unsupported remote address");
            return false;
        }

        var rawQuery = httpContext.Request.QueryString.Value;
        if (string.IsNullOrEmpty(rawQuery))
        {
            error = new AnnounceError(StatusCodes.Status400BadRequest, "missing query string");
            return false;
        }

        var hashes = new List<InfoHashKey>();
        var dedupe = new HashSet<InfoHashKey>();
        var queryPasskey = passkey;
        Span<byte> infoHashBytes = stackalloc byte[20];

        var query = rawQuery.AsSpan();
        if (query.Length > 0 && query[0] == '?')
        {
            query = query[1..];
        }

        while (!query.IsEmpty)
        {
            var ampersand = query.IndexOf('&');
            var segment = ampersand >= 0 ? query[..ampersand] : query;
            query = ampersand >= 0 ? query[(ampersand + 1)..] : [];

            var equals = segment.IndexOf('=');
            if (equals <= 0)
            {
                continue;
            }

            var key = segment[..equals];
            var value = segment[(equals + 1)..];

            if (key.SequenceEqual("info_hash"))
            {
                if (!TrackerRequestParsing.TryDecodeExact20Bytes(value, infoHashBytes))
                {
                    error = new AnnounceError(StatusCodes.Status400BadRequest, "invalid info_hash");
                    return false;
                }

                var infoHash = InfoHashKey.FromBytes(infoHashBytes);
                if (dedupe.Add(infoHash))
                {
                    hashes.Add(infoHash);
                }
            }
            else if (key.SequenceEqual("passkey") && string.IsNullOrWhiteSpace(queryPasskey))
            {
                queryPasskey = value.ToString();
            }
        }

        request = new ScrapeRequest(queryPasskey, hashes.ToArray());
        return true;
    }
}

public sealed class ScrapeRequestValidator(IOptions<TrackerSecurityOptions> securityOptions) : IScrapeRequestValidator
{
    public AnnounceValidationResult Validate(in ScrapeRequest request)
    {
        if (request.InfoHashes.Length == 0)
        {
            return AnnounceValidationResult.Fail(StatusCodes.Status400BadRequest, "missing info_hash");
        }

        if (request.InfoHashes.Length > securityOptions.Value.MaxScrapeInfoHashes)
        {
            return AnnounceValidationResult.Fail(StatusCodes.Status400BadRequest, "too many info_hash values");
        }

        return AnnounceValidationResult.Success();
    }
}

public sealed class CachedAccessPolicyResolver(IAccessSnapshotProvider accessSnapshotProvider, IClock clock) : IAccessPolicyResolver
{
    public async ValueTask<AccessResolution> ResolveAsync(AnnounceRequest request, CancellationToken cancellationToken)
    {
        var infoHash = request.InfoHash.ToHexString();
        var policy = await accessSnapshotProvider.GetTorrentPolicyAsync(infoHash, cancellationToken);
        if (policy is null || !policy.IsEnabled)
        {
            return AccessResolution.Deny("torrent not registered");
        }

        var torrentBan = await accessSnapshotProvider.GetBanRuleAsync("torrent", infoHash, cancellationToken);
        if (IsBanActive(torrentBan))
        {
            return AccessResolution.Deny(torrentBan!.Reason);
        }

        if (!policy.IsPrivate)
        {
            return AccessResolution.Allow(policy);
        }

        if (string.IsNullOrWhiteSpace(request.Passkey))
        {
            return AccessResolution.Deny("passkey required");
        }

        var passkey = await accessSnapshotProvider.GetPasskeyAsync(request.Passkey, cancellationToken);
        if (passkey is null || passkey.IsRevoked)
        {
            return AccessResolution.Deny("invalid passkey");
        }

        if (passkey.ExpiresAtUtc is { } expiresAt && expiresAt <= clock.UtcNow)
        {
            return AccessResolution.Deny("expired passkey");
        }

        var passkeyBan = await accessSnapshotProvider.GetBanRuleAsync("passkey", passkey.Passkey, cancellationToken);
        if (IsBanActive(passkeyBan))
        {
            return AccessResolution.Deny(passkeyBan!.Reason);
        }

        var userBan = await accessSnapshotProvider.GetBanRuleAsync("user", passkey.UserId.ToString("D"), cancellationToken);
        if (IsBanActive(userBan))
        {
            return AccessResolution.Deny(userBan!.Reason);
        }

        var permissions = await accessSnapshotProvider.GetUserPermissionAsync(passkey.UserId, cancellationToken);
        if (permissions is null)
        {
            return AccessResolution.Deny("permissions unavailable");
        }

        if (!permissions.CanUsePrivateTracker)
        {
            return AccessResolution.Deny("private tracker access denied");
        }

        if (request.IsSeeder && !permissions.CanSeed)
        {
            return AccessResolution.Deny("seeding is not permitted");
        }

        if (!request.IsSeeder && !permissions.CanLeech)
        {
            return AccessResolution.Deny("leeching is not permitted");
        }

        return AccessResolution.Allow(policy);
    }

    private bool IsBanActive(BanRuleDto? banRule)
    {
        return banRule is not null && (banRule.ExpiresAtUtc is null || banRule.ExpiresAtUtc > clock.UtcNow);
    }
}

public sealed class CachedScrapeAccessPolicyResolver(IAccessSnapshotProvider accessSnapshotProvider, IClock clock) : IScrapeAccessPolicyResolver
{
    public async ValueTask<ScrapeAccessResolution> ResolveAsync(string? passkey, InfoHashKey infoHash, CancellationToken cancellationToken)
    {
        var infoHashHex = infoHash.ToHexString();
        var policy = await accessSnapshotProvider.GetTorrentPolicyAsync(infoHashHex, cancellationToken);
        if (policy is null || !policy.IsEnabled || !policy.AllowScrape)
        {
            return ScrapeAccessResolution.Deny();
        }

        var torrentBan = await accessSnapshotProvider.GetBanRuleAsync("torrent", infoHashHex, cancellationToken);
        if (IsBanActive(torrentBan))
        {
            return ScrapeAccessResolution.Deny();
        }

        if (!policy.IsPrivate)
        {
            return ScrapeAccessResolution.Allow(policy);
        }

        if (string.IsNullOrWhiteSpace(passkey))
        {
            return ScrapeAccessResolution.Deny();
        }

        var passkeyAccess = await accessSnapshotProvider.GetPasskeyAsync(passkey, cancellationToken);
        if (passkeyAccess is null || passkeyAccess.IsRevoked)
        {
            return ScrapeAccessResolution.Deny();
        }

        if (passkeyAccess.ExpiresAtUtc is { } expiresAt && expiresAt <= clock.UtcNow)
        {
            return ScrapeAccessResolution.Deny();
        }

        var passkeyBan = await accessSnapshotProvider.GetBanRuleAsync("passkey", passkeyAccess.Passkey, cancellationToken);
        if (IsBanActive(passkeyBan))
        {
            return ScrapeAccessResolution.Deny();
        }

        var userBan = await accessSnapshotProvider.GetBanRuleAsync("user", passkeyAccess.UserId.ToString("D"), cancellationToken);
        if (IsBanActive(userBan))
        {
            return ScrapeAccessResolution.Deny();
        }

        var permissions = await accessSnapshotProvider.GetUserPermissionAsync(passkeyAccess.UserId, cancellationToken);
        if (permissions is null || !permissions.CanUsePrivateTracker || !permissions.CanScrape)
        {
            return ScrapeAccessResolution.Deny();
        }

        return ScrapeAccessResolution.Allow(policy);
    }

    private bool IsBanActive(BanRuleDto? banRule)
    {
        return banRule is not null && (banRule.ExpiresAtUtc is null || banRule.ExpiresAtUtc > clock.UtcNow);
    }
}

internal static class TrackerRequestParsing
{
    public static bool TryGetRemoteEndpoint(HttpContext httpContext, bool allowIPv6Peers, out PeerEndpoint endpoint)
    {
        endpoint = default;

        var remoteAddress = httpContext.Connection.RemoteIpAddress;
        if (remoteAddress is null)
        {
            return false;
        }

        if (remoteAddress.IsIPv4MappedToIPv6)
        {
            remoteAddress = remoteAddress.MapToIPv4();
        }

        if (remoteAddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            Span<byte> bytes = stackalloc byte[4];
            if (!remoteAddress.TryWriteBytes(bytes, out _))
            {
                return false;
            }

            endpoint = PeerEndpoint.FromIPv4(BinaryPrimitives.ReadUInt32BigEndian(bytes), (ushort)httpContext.Connection.RemotePort);
            return true;
        }

        if (!allowIPv6Peers || remoteAddress.AddressFamily != System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            return false;
        }

        Span<byte> ipv6Bytes = stackalloc byte[16];
        if (!remoteAddress.TryWriteBytes(ipv6Bytes, out _))
        {
            return false;
        }

        endpoint = PeerEndpoint.FromIPv6(ipv6Bytes, (ushort)httpContext.Connection.RemotePort);
        return true;
    }

    public static bool TryDecodeExact20Bytes(ReadOnlySpan<char> source, Span<byte> destination)
    {
        var written = 0;
        for (var index = 0; index < source.Length; index++)
        {
            if (written >= destination.Length)
            {
                return false;
            }

            if (source[index] == '%')
            {
                if (index + 2 >= source.Length)
                {
                    return false;
                }

                if (!byte.TryParse(source.Slice(index + 1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out destination[written]))
                {
                    return false;
                }

                index += 2;
                written++;
                continue;
            }

            destination[written++] = (byte)source[index];
        }

        return written == 20;
    }

    public static TrackerEvent ParseEvent(ReadOnlySpan<char> value)
    {
        if (value.SequenceEqual("started"))
        {
            return TrackerEvent.Started;
        }

        if (value.SequenceEqual("completed"))
        {
            return TrackerEvent.Completed;
        }

        if (value.SequenceEqual("stopped"))
        {
            return TrackerEvent.Stopped;
        }

        return TrackerEvent.None;
    }
}

public sealed class ChannelAnnounceTelemetryWriter : IAnnounceTelemetryWriter
{
    private readonly Channel<AnnounceTelemetryRecord> _channel = Channel.CreateBounded<AnnounceTelemetryRecord>(new BoundedChannelOptions(100_000)
    {
        SingleReader = true,
        SingleWriter = false,
        FullMode = BoundedChannelFullMode.DropWrite
    });

    private int _queueLength;

    public int QueueLength => Volatile.Read(ref _queueLength);

    public ChannelReader<AnnounceTelemetryRecord> Reader => _channel.Reader;

    public bool TryWrite(AnnounceTelemetryRecord telemetryRecord)
    {
        var written = _channel.Writer.TryWrite(telemetryRecord);
        if (written)
        {
            Interlocked.Increment(ref _queueLength);
        }
        else
        {
            TrackerDiagnostics.TelemetryDropped.Add(1);
        }

        return written;
    }

    public void MarkDequeued(int count) => Interlocked.Add(ref _queueLength, -count);
}

public interface IBencodeResponseWriter
{
    ValueTask WriteAnnounceSuccessAsync(HttpResponse response, AnnounceSuccess success, CancellationToken cancellationToken);
    ValueTask WriteScrapeSuccessAsync(HttpResponse response, ScrapeSuccess success, CancellationToken cancellationToken);
    ValueTask WriteFailureAsync(HttpResponse response, int statusCode, string reason, CancellationToken cancellationToken);
}

public sealed class AnnounceBencodeResponseWriter : IBencodeResponseWriter
{
    private static ReadOnlySpan<byte> CompleteKey => "8:complete"u8;
    private static ReadOnlySpan<byte> DownloadedKey => "10:downloaded"u8;
    private static ReadOnlySpan<byte> FailureKey => "14:failure reason"u8;
    private static ReadOnlySpan<byte> FilesKey => "5:files"u8;
    private static ReadOnlySpan<byte> IncompleteKey => "10:incomplete"u8;
    private static ReadOnlySpan<byte> IntervalKey => "8:interval"u8;
    private static ReadOnlySpan<byte> PeersKey => "5:peers"u8;
    private static ReadOnlySpan<byte> Peers6Key => "6:peers6"u8;
    private static ReadOnlySpan<byte> WarningMessageKey => "15:warning message"u8;

    private static ReadOnlySpan<byte> IpKey => "2:ip"u8;
    private static ReadOnlySpan<byte> PeerIdDictKey => "7:peer id"u8;
    private static ReadOnlySpan<byte> PortKey => "4:port"u8;

    public async ValueTask WriteAnnounceSuccessAsync(HttpResponse response, AnnounceSuccess success, CancellationToken cancellationToken)
    {
        response.StatusCode = StatusCodes.Status200OK;
        response.ContentType = "text/plain";

        var writer = response.BodyWriter;
        BencodePipeWriter.WriteDictionaryStart(writer);

        writer.Write(CompleteKey);
        BencodePipeWriter.WriteInteger(writer, success.SeederCount);

        writer.Write(IncompleteKey);
        BencodePipeWriter.WriteInteger(writer, success.LeecherCount);

        writer.Write(IntervalKey);
        BencodePipeWriter.WriteInteger(writer, success.IntervalSeconds);

        if (success.Compact)
        {
            writer.Write(PeersKey);
            BencodePipeWriter.WriteLength(writer, success.PeerSelection.Peers.Count * 6);
            BencodePipeWriter.WriteByte(writer, (byte)':');

            foreach (var peer in success.PeerSelection.Peers.AsSpan())
            {
                var span = writer.GetSpan(6);
                peer.Endpoint.WriteCompactBytes(span[..6]);
                writer.Advance(6);
            }

            if (success.PeerSelection.Peers6.Count > 0)
            {
                writer.Write(Peers6Key);
                BencodePipeWriter.WriteLength(writer, success.PeerSelection.Peers6.Count * 18);
                BencodePipeWriter.WriteByte(writer, (byte)':');

                foreach (var peer in success.PeerSelection.Peers6.AsSpan())
                {
                    var span = writer.GetSpan(18);
                    peer.Endpoint.WriteCompactBytes(span[..18]);
                    writer.Advance(18);
                }
            }
        }
        else
        {
            writer.Write(PeersKey);
            WritePeerDictionaryList(writer, success.PeerSelection.Peers.AsSpan());

            if (success.PeerSelection.Peers6.Count > 0)
            {
                writer.Write(Peers6Key);
                WritePeerDictionaryList(writer, success.PeerSelection.Peers6.AsSpan());
            }
        }

        if (success.WarningMessage is not null)
        {
            writer.Write(WarningMessageKey);
            BencodePipeWriter.WriteAsciiString(writer, success.WarningMessage);
        }

        BencodePipeWriter.WriteDictionaryEnd(writer);
        await writer.FlushAsync(cancellationToken);
    }

    private static void WritePeerDictionaryList(System.IO.Pipelines.PipeWriter writer, ReadOnlySpan<SelectedPeer> peers)
    {
        BencodePipeWriter.WriteListStart(writer);

        Span<byte> peerIdBytes = stackalloc byte[20];
        foreach (var peer in peers)
        {
            BencodePipeWriter.WriteDictionaryStart(writer);

            writer.Write(IpKey);
            BencodePipeWriter.WriteAsciiString(writer, peer.Endpoint.ToIpString());

            writer.Write(PeerIdDictKey);
            peer.PeerId.WriteBytes(peerIdBytes);
            BencodePipeWriter.WriteAsciiString(writer, peerIdBytes);

            writer.Write(PortKey);
            BencodePipeWriter.WriteInteger(writer, peer.Port);

            BencodePipeWriter.WriteDictionaryEnd(writer);
        }

        BencodePipeWriter.WriteListEnd(writer);
    }

    public async ValueTask WriteScrapeSuccessAsync(HttpResponse response, ScrapeSuccess success, CancellationToken cancellationToken)
    {
        response.StatusCode = StatusCodes.Status200OK;
        response.ContentType = "text/plain";

        var writer = response.BodyWriter;
        var infoHashBytes = new byte[20];
        BencodePipeWriter.WriteDictionaryStart(writer);
        writer.Write(FilesKey);
        BencodePipeWriter.WriteDictionaryStart(writer);

        foreach (var file in success.Files)
        {
            file.InfoHash.WriteBytes(infoHashBytes);
            BencodePipeWriter.WriteAsciiString(writer, infoHashBytes);
            BencodePipeWriter.WriteDictionaryStart(writer);

            writer.Write(CompleteKey);
            BencodePipeWriter.WriteInteger(writer, file.SeederCount);

            writer.Write(DownloadedKey);
            BencodePipeWriter.WriteInteger(writer, file.DownloadedCount);

            writer.Write(IncompleteKey);
            BencodePipeWriter.WriteInteger(writer, file.LeecherCount);

            BencodePipeWriter.WriteDictionaryEnd(writer);
        }

        BencodePipeWriter.WriteDictionaryEnd(writer);
        BencodePipeWriter.WriteDictionaryEnd(writer);
        await writer.FlushAsync(cancellationToken);
    }

    public async ValueTask WriteFailureAsync(HttpResponse response, int statusCode, string reason, CancellationToken cancellationToken)
    {
        response.StatusCode = statusCode;
        response.ContentType = "text/plain";

        var writer = response.BodyWriter;
        BencodePipeWriter.WriteDictionaryStart(writer);
        writer.Write(FailureKey);
        BencodePipeWriter.WriteAsciiString(writer, reason);
        BencodePipeWriter.WriteDictionaryEnd(writer);
        await writer.FlushAsync(cancellationToken);
    }
}

public sealed class AnnounceTelemetryBackgroundService(
    ChannelAnnounceTelemetryWriter telemetryWriter,
    IPostgresConnectionFactory postgresConnectionFactory,
    IOptions<TelemetryBatchingOptions> batchingOptions,
    IReadinessState readinessState,
    IClock clock,
    ILogger<AnnounceTelemetryBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        readinessState.MarkReady();

        var batch = new List<AnnounceTelemetryRecord>(batchingOptions.Value.BatchSize);
        var flushDelay = TimeSpan.FromMilliseconds(batchingOptions.Value.FlushIntervalMilliseconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            var deadline = clock.UtcNow + flushDelay;

            while (batch.Count < batchingOptions.Value.BatchSize && clock.UtcNow < deadline)
            {
                var remaining = deadline - clock.UtcNow;
                if (remaining <= TimeSpan.Zero)
                {
                    break;
                }

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                timeoutCts.CancelAfter(remaining);

                try
                {
                    var item = await telemetryWriter.Reader.ReadAsync(timeoutCts.Token);
                    batch.Add(item);
                }
                catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }

            if (batch.Count == 0)
            {
                continue;
            }

            try
            {
                await PersistBatchAsync(batch, postgresConnectionFactory, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                TrackerDiagnostics.TelemetryDropped.Add(batch.Count);
                logger.LogError(ex, "Failed to persist telemetry batch of {Count} records.", batch.Count);
            }

            telemetryWriter.MarkDequeued(batch.Count);
            batch.Clear();
        }
    }

    private static async Task PersistBatchAsync(
        IReadOnlyCollection<AnnounceTelemetryRecord> batch,
        IPostgresConnectionFactory postgresConnectionFactory,
        CancellationToken cancellationToken)
    {
        await using var connection = await postgresConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var batchCommand = new NpgsqlBatch(connection);

        foreach (var telemetry in batch)
        {
            var command = new NpgsqlBatchCommand(
                """
                insert into announce_telemetry
                    (node_id, info_hash, peer_id, passkey, event_name, requested_peers, returned_peers, occurred_at_utc)
                values
                    ($1, $2, $3, $4, $5, $6, $7, $8)
                """);

            command.Parameters.Add(new NpgsqlParameter { Value = telemetry.NodeId });
            command.Parameters.Add(new NpgsqlParameter { Value = telemetry.InfoHash });
            command.Parameters.Add(new NpgsqlParameter { Value = telemetry.PeerId });
            command.Parameters.Add(new NpgsqlParameter { Value = (object?)telemetry.Passkey ?? DBNull.Value });
            command.Parameters.Add(new NpgsqlParameter { Value = telemetry.Event.ToString().ToLowerInvariant() });
            command.Parameters.Add(new NpgsqlParameter { Value = telemetry.RequestedPeers });
            command.Parameters.Add(new NpgsqlParameter { Value = telemetry.ReturnedPeers });
            command.Parameters.Add(new NpgsqlParameter { Value = telemetry.OccurredAtUtc.UtcDateTime });
            batchCommand.BatchCommands.Add(command);
        }

        await batchCommand.ExecuteNonQueryAsync(cancellationToken);
    }
}

public sealed class GatewayWarmupService(
    IServiceProvider serviceProvider,
    IReadinessState readinessState,
    IGatewayDependencyState dependencyState) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await StartupBootstrap.WaitForPostgresAsync(serviceProvider, "tracker-gateway", cancellationToken);
        dependencyState.UpdatePostgres(true, DateTimeOffset.UtcNow);

        await StartupBootstrap.WaitForRedisAsync(serviceProvider, "tracker-gateway", cancellationToken);
        dependencyState.UpdateRedis(true, DateTimeOffset.UtcNow);

        readinessState.MarkReady();
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

public static class GatewayInfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddGatewayInfrastructure(this IServiceCollection services)
    {
        services.AddMemoryCache();
        services.AddSingleton<IPasskeyRedactor, TrackerPasskeyRedactor>();
        services.AddSingleton<ITrackerUrlBuilder, TrackerUrlBuilder>();
        services.AddSingleton<IAnnounceRequestParser, AnnounceRequestParser>();
        services.AddSingleton<IAnnounceRequestValidator, AnnounceRequestValidator>();
        services.AddSingleton<IScrapeRequestParser, ScrapeRequestParser>();
        services.AddSingleton<IScrapeRequestValidator, ScrapeRequestValidator>();
        services.AddSingleton<IAnnounceAbuseGuard, AnnounceAbuseGuard>();
        services.AddSingleton<IScrapeAbuseGuard, ScrapeAbuseGuard>();
        services.AddSingleton<IGatewayDependencyState, GatewayDependencyState>();
        services.AddSingleton<AccessRefreshQueue>();
        services.AddSingleton<IAccessSnapshotStore, RedisPostgresAccessSnapshotStore>();
        services.AddSingleton<HybridAccessSnapshotProvider>();
        services.AddSingleton<IAccessSnapshotProvider>(static serviceProvider => serviceProvider.GetRequiredService<HybridAccessSnapshotProvider>());
        services.AddSingleton<IAccessInvalidationPublisher, RedisAccessInvalidationPublisher>();
        services.AddSingleton<IAccessPolicyResolver, CachedAccessPolicyResolver>();
        services.AddSingleton<IScrapeAccessPolicyResolver, CachedScrapeAccessPolicyResolver>();
        services.AddSingleton<IBencodeResponseWriter, AnnounceBencodeResponseWriter>();
        services.AddSingleton<ChannelAnnounceTelemetryWriter>();
        services.AddSingleton<IAnnounceTelemetryWriter>(static serviceProvider => serviceProvider.GetRequiredService<ChannelAnnounceTelemetryWriter>());
        services.AddSingleton<IAnnounceService, AnnounceService>();
        services.AddSingleton<IScrapeService, ScrapeService>();
        services.AddHostedService<AccessSnapshotHydrationService>();
        services.AddHostedService<AccessInvalidationSubscriberService>();
        services.AddHostedService<GatewayDependencyMonitorService>();
        services.AddHostedService<AnnounceTelemetryBackgroundService>();
        services.AddHostedService<GatewayWarmupService>();
        return services;
    }

    public static IServiceCollection AddGatewayObservabilityServices(this IServiceCollection services)
    {
        services.AddHostedService<SwarmStoreGaugeRegistrationService>();
        services.AddHostedService<LiveStatsEmitter>();
        return services;
    }
}

internal sealed class SwarmStoreGaugeRegistrationService(
    IRuntimeSwarmStore runtimeSwarmStore,
    ChannelAnnounceTelemetryWriter telemetryWriter) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var store = (Tracker.Gateway.Runtime.PartitionedRuntimeSwarmStore)runtimeSwarmStore;
        TrackerDiagnostics.RegisterSwarmStoreGauges(store.GetTotalPeerCount, store.GetTotalSwarmCount);
        TrackerDiagnostics.RegisterTelemetryQueueGauge(() => telemetryWriter.QueueLength);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
