namespace BeeTracker.BuildingBlocks.Abstractions.Options;

/// <summary>
/// Defines how the tracker handles protocol edge cases and client compatibility.
/// Configured globally; can be overridden per-torrent via TorrentPolicyDto.
/// </summary>
public sealed class TrackerCompatibilityOptions
{
    public const string SectionName = "BeeTracker:TrackerCompatibility";

    /// <summary>Global client compatibility mode. Affects tolerance and fallback behavior.</summary>
    public ClientCompatibilityMode CompatibilityMode { get; init; } = ClientCompatibilityMode.Standard;

    /// <summary>Global protocol strictness profile. Affects parser/validator behavior.</summary>
    public ProtocolStrictnessProfile StrictnessProfile { get; init; } = ProtocolStrictnessProfile.Balanced;
}

/// <summary>
/// Client compatibility mode controls tolerance of optional parameters
/// and fallback behavior for compact/non-compact negotiation.
/// </summary>
public enum ClientCompatibilityMode
{
    /// <summary>
    /// Strict: reject non-compact when RequireCompactResponses is set,
    /// reject unknown events, no tolerance for missing optional fields.
    /// </summary>
    Strict = 0,

    /// <summary>
    /// Standard: default behavior. Follows spec strictly but allows
    /// optional parameters to be absent with sensible defaults.
    /// </summary>
    Standard = 1,

    /// <summary>
    /// Compatibility: allows non-compact fallback even when compact is preferred,
    /// tolerates unknown event values (maps to None), tolerates negative numwant
    /// (clamps to 0), and emits warnings instead of errors for recoverable issues.
    /// </summary>
    Compatibility = 2
}

/// <summary>
/// Protocol strictness profile controls parser and validator sensitivity.
/// </summary>
public enum ProtocolStrictnessProfile
{
    /// <summary>
    /// Strict: rejects malformed parameters immediately, no tolerance
    /// for non-standard query encodings, strict info_hash/peer_id validation.
    /// </summary>
    Strict = 0,

    /// <summary>
    /// Balanced: default. Rejects clearly malformed requests but accepts
    /// minor variations (e.g. extra query parameters are ignored).
    /// </summary>
    Balanced = 1,

    /// <summary>
    /// Permissive: maximum tolerance. Clamps out-of-range values instead of
    /// rejecting, accepts negative numwant (clamps to 0), tolerates minor
    /// encoding deviations where safe. Logs anomalies for operator review.
    /// </summary>
    Permissive = 2
}
