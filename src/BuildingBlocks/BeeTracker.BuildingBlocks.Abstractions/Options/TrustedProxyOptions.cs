namespace BeeTracker.BuildingBlocks.Abstractions.Options;

public sealed class TrustedProxyOptions
{
    public const string SectionName = "BeeTracker:TrustedProxy";

    public int ForwardLimit { get; init; } = 1;
    public string[] KnownProxies { get; init; } = [];
    public string[] KnownNetworks { get; init; } = [];
}
