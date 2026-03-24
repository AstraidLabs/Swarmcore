using System.Buffers;
using System.Buffers.Binary;
using System.Net;
using BeeTracker.Contracts.Configuration;

namespace Tracker.Gateway.Application.Announce;

public enum TrackerEvent : byte
{
    None = 0,
    Started = 1,
    Completed = 2,
    Stopped = 3
}

public readonly record struct InfoHashKey(ulong Part0, ulong Part1, uint Part2)
{
    public static InfoHashKey FromBytes(ReadOnlySpan<byte> value)
    {
        if (value.Length != 20)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Info hash must be 20 bytes.");
        }

        return new InfoHashKey(
            BinaryPrimitives.ReadUInt64BigEndian(value[..8]),
            BinaryPrimitives.ReadUInt64BigEndian(value[8..16]),
            BinaryPrimitives.ReadUInt32BigEndian(value[16..20]));
    }

    public string ToHexString()
    {
        Span<byte> bytes = stackalloc byte[20];
        WriteBytes(bytes);

        Span<char> chars = stackalloc char[40];
        Convert.TryToHexString(bytes, chars, out _);
        return new string(chars);
    }

    public void WriteBytes(Span<byte> destination)
    {
        if (destination.Length < 20)
        {
            throw new ArgumentOutOfRangeException(nameof(destination), "Destination must be at least 20 bytes.");
        }

        BinaryPrimitives.WriteUInt64BigEndian(destination[..8], Part0);
        BinaryPrimitives.WriteUInt64BigEndian(destination[8..16], Part1);
        BinaryPrimitives.WriteUInt32BigEndian(destination[16..20], Part2);
    }
}

public readonly record struct PeerIdKey(ulong Part0, ulong Part1, uint Part2)
{
    public static PeerIdKey FromBytes(ReadOnlySpan<byte> value)
    {
        if (value.Length != 20)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Peer id must be 20 bytes.");
        }

        return new PeerIdKey(
            BinaryPrimitives.ReadUInt64BigEndian(value[..8]),
            BinaryPrimitives.ReadUInt64BigEndian(value[8..16]),
            BinaryPrimitives.ReadUInt32BigEndian(value[16..20]));
    }

    public string ToHexString()
    {
        Span<byte> bytes = stackalloc byte[20];
        BinaryPrimitives.WriteUInt64BigEndian(bytes[..8], Part0);
        BinaryPrimitives.WriteUInt64BigEndian(bytes[8..16], Part1);
        BinaryPrimitives.WriteUInt32BigEndian(bytes[16..20], Part2);

        Span<char> chars = stackalloc char[40];
        Convert.TryToHexString(bytes, chars, out _);
        return new string(chars);
    }

    public void WriteBytes(Span<byte> destination)
    {
        if (destination.Length < 20)
        {
            throw new ArgumentOutOfRangeException(nameof(destination), "Destination must be at least 20 bytes.");
        }

        BinaryPrimitives.WriteUInt64BigEndian(destination[..8], Part0);
        BinaryPrimitives.WriteUInt64BigEndian(destination[8..16], Part1);
        BinaryPrimitives.WriteUInt32BigEndian(destination[16..20], Part2);
    }
}

public enum PeerAddressFamily : byte
{
    IPv4 = 1,
    IPv6 = 2
}

public readonly record struct PeerEndpoint(
    PeerAddressFamily AddressFamily,
    uint AddressPart0NetworkOrder,
    uint AddressPart1NetworkOrder,
    uint AddressPart2NetworkOrder,
    uint AddressPart3NetworkOrder,
    ushort Port)
{
    public bool IsEmpty => Port == 0 || (AddressPart0NetworkOrder | AddressPart1NetworkOrder | AddressPart2NetworkOrder | AddressPart3NetworkOrder) == 0;

    public static PeerEndpoint FromIPv4(uint addressV4NetworkOrder, ushort port) => new(
        PeerAddressFamily.IPv4,
        addressV4NetworkOrder,
        0,
        0,
        0,
        port);

    public static PeerEndpoint FromIPv6(ReadOnlySpan<byte> addressBytes, ushort port)
    {
        if (addressBytes.Length != 16)
        {
            throw new ArgumentOutOfRangeException(nameof(addressBytes), "IPv6 address must be 16 bytes.");
        }

        return new PeerEndpoint(
            PeerAddressFamily.IPv6,
            BinaryPrimitives.ReadUInt32BigEndian(addressBytes[..4]),
            BinaryPrimitives.ReadUInt32BigEndian(addressBytes[4..8]),
            BinaryPrimitives.ReadUInt32BigEndian(addressBytes[8..12]),
            BinaryPrimitives.ReadUInt32BigEndian(addressBytes[12..16]),
            port);
    }

    public int CompactLength => AddressFamily == PeerAddressFamily.IPv6 ? 18 : 6;

    public bool Matches(in PeerEndpoint other)
    {
        return AddressFamily == other.AddressFamily
            && AddressPart0NetworkOrder == other.AddressPart0NetworkOrder
            && AddressPart1NetworkOrder == other.AddressPart1NetworkOrder
            && AddressPart2NetworkOrder == other.AddressPart2NetworkOrder
            && AddressPart3NetworkOrder == other.AddressPart3NetworkOrder
            && Port == other.Port;
    }

    public void WriteCompactBytes(Span<byte> destination)
    {
        if (destination.Length < CompactLength)
        {
            throw new ArgumentOutOfRangeException(nameof(destination), "Destination is too small for compact peer encoding.");
        }

        if (AddressFamily == PeerAddressFamily.IPv4)
        {
            BinaryPrimitives.WriteUInt32BigEndian(destination[..4], AddressPart0NetworkOrder);
            BinaryPrimitives.WriteUInt16BigEndian(destination[4..6], Port);
            return;
        }

        BinaryPrimitives.WriteUInt32BigEndian(destination[..4], AddressPart0NetworkOrder);
        BinaryPrimitives.WriteUInt32BigEndian(destination[4..8], AddressPart1NetworkOrder);
        BinaryPrimitives.WriteUInt32BigEndian(destination[8..12], AddressPart2NetworkOrder);
        BinaryPrimitives.WriteUInt32BigEndian(destination[12..16], AddressPart3NetworkOrder);
        BinaryPrimitives.WriteUInt16BigEndian(destination[16..18], Port);
    }

    public IPAddress ToIpAddress()
    {
        Span<byte> bytes = stackalloc byte[AddressFamily == PeerAddressFamily.IPv6 ? 16 : 4];
        if (AddressFamily == PeerAddressFamily.IPv4)
        {
            BinaryPrimitives.WriteUInt32BigEndian(bytes, AddressPart0NetworkOrder);
        }
        else
        {
            BinaryPrimitives.WriteUInt32BigEndian(bytes[..4], AddressPart0NetworkOrder);
            BinaryPrimitives.WriteUInt32BigEndian(bytes[4..8], AddressPart1NetworkOrder);
            BinaryPrimitives.WriteUInt32BigEndian(bytes[8..12], AddressPart2NetworkOrder);
            BinaryPrimitives.WriteUInt32BigEndian(bytes[12..16], AddressPart3NetworkOrder);
        }

        return new IPAddress(bytes);
    }

    public string ToIpString() => ToIpAddress().ToString();
}

public readonly record struct AnnounceRequest(
    InfoHashKey InfoHash,
    PeerIdKey PeerId,
    PeerEndpoint Endpoint,
    long Uploaded,
    long Downloaded,
    long Left,
    int RequestedPeers,
    bool Compact,
    bool NoPeerId,
    TrackerEvent Event,
    string? Passkey,
    string? Key,
    string? TrackerId)
{
    public bool IsSeeder => Left == 0;
}

public readonly record struct AnnounceError(int StatusCode, string FailureReason);

public readonly record struct AnnounceValidationResult(bool IsValid, AnnounceError Error)
{
    public static AnnounceValidationResult Success() => new(true, default);
    public static AnnounceValidationResult Fail(int statusCode, string failureReason) => new(false, new AnnounceError(statusCode, failureReason));
}

public readonly record struct AccessResolution(bool IsAllowed, TorrentPolicyDto Policy, string FailureReason)
{
    public static AccessResolution Allow(TorrentPolicyDto policy) => new(true, policy, string.Empty);
    public static AccessResolution Deny(string failureReason) => new(false, default!, failureReason);
}

public readonly record struct SwarmCounts(int SeederCount, int LeecherCount, int DownloadedCount = 0);

public readonly record struct SelectedPeer(PeerEndpoint Endpoint, PeerIdKey PeerId = default)
{
    public PeerAddressFamily AddressFamily => Endpoint.AddressFamily;
    public ushort Port => Endpoint.Port;
}

public struct PeerSelectionResult : IDisposable
{
    public PeerSelectionResult(SelectedPeer[] buffer, int count, bool pooled = false)
    {
        Buffer = buffer;
        Count = count;
        IsPooled = pooled;
    }

    public SelectedPeer[]? Buffer { get; private set; }
    public int Count { get; private set; }
    public bool IsPooled { get; private set; }

    public ReadOnlySpan<SelectedPeer> AsSpan() => Buffer is null ? [] : Buffer.AsSpan(0, Count);

    public void Dispose()
    {
        if (Buffer is null || Buffer.Length == 0 || !IsPooled)
        {
            return;
        }

        ArrayPool<SelectedPeer>.Shared.Return(Buffer, clearArray: false);
        Buffer = null;
        Count = 0;
        IsPooled = false;
    }
}

public struct AnnouncePeerSelection : IDisposable
{
    public AnnouncePeerSelection(PeerSelectionResult peers, PeerSelectionResult peers6)
    {
        Peers = peers;
        Peers6 = peers6;
    }

    public PeerSelectionResult Peers { get; private set; }
    public PeerSelectionResult Peers6 { get; private set; }

    public void Dispose()
    {
        Peers.Dispose();
        Peers6.Dispose();
    }
}

public readonly record struct AnnounceSuccess(
    int IntervalSeconds,
    int SeederCount,
    int LeecherCount,
    AnnouncePeerSelection PeerSelection,
    string? WarningMessage = null,
    bool Compact = true,
    bool NoPeerId = false,
    string? TrackerId = null,
    int MinIntervalSeconds = 0);

public readonly record struct AnnounceTelemetryRecord(
    string NodeId,
    string InfoHash,
    string PeerId,
    string? Passkey,
    TrackerEvent Event,
    int RequestedPeers,
    int ReturnedPeers,
    DateTimeOffset OccurredAtUtc);
