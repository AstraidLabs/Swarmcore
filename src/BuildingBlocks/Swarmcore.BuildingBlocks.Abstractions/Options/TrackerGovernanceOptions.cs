namespace Swarmcore.BuildingBlocks.Abstractions.Options;

/// <summary>
/// Runtime governance controls that can be toggled at the node level.
/// These flags control operational behavior without requiring restarts.
/// </summary>
public sealed class TrackerGovernanceOptions
{
    public const string SectionName = "Swarmcore:TrackerGovernance";

    /// <summary>When true, the node rejects all announce requests with 503. Scrape may still be allowed.</summary>
    public bool AnnounceDisabled { get; set; }

    /// <summary>When true, the node rejects all scrape requests with 503.</summary>
    public bool ScrapeDisabled { get; set; }

    /// <summary>When true, the node is in global maintenance mode: all tracker protocol requests return 503.</summary>
    public bool GlobalMaintenanceMode { get; set; }

    /// <summary>When true, the tracker operates in read-only mode: announce mutations are skipped but peers are still returned.</summary>
    public bool ReadOnlyMode { get; set; }

    /// <summary>When true, emergency abuse mitigation is active: rate limits are halved across the board.</summary>
    public bool EmergencyAbuseMitigation { get; set; }

    /// <summary>When true, UDP tracker is disabled: all UDP requests are silently dropped.</summary>
    public bool UdpDisabled { get; set; }

    /// <summary>When true, IPv6 peer handling is frozen: new IPv6 peers are not accepted.</summary>
    public bool IPv6Frozen { get; set; }

    /// <summary>When true, policy changes are frozen: cache invalidations are ignored until unfrozen.</summary>
    public bool PolicyFreezeMode { get; set; }
}
