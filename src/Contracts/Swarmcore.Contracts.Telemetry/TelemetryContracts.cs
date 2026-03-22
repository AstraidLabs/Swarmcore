namespace Swarmcore.Contracts.Telemetry;

public sealed record AnnounceTelemetryEvent(
    string NodeId,
    string InfoHash,
    string? Passkey,
    string EventName,
    int RequestedPeers,
    DateTimeOffset OccurredAtUtc);

public sealed record ScrapeTelemetryEvent(
    string NodeId,
    string InfoHash,
    DateTimeOffset OccurredAtUtc);

public sealed record AccessDecisionTelemetryEvent(
    string NodeId,
    string Subject,
    string Decision,
    string Reason,
    DateTimeOffset OccurredAtUtc);
