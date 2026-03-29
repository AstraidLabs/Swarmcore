namespace BeeTracker.BuildingBlocks.Abstractions.Options;

public sealed class PostgresOptions
{
    public const string SectionName = "BeeTracker:Postgres";

    public string ConnectionString { get; init; } = string.Empty;
    public bool MigrateOnStart { get; init; } = true;
    public bool PersistTelemetry { get; init; } = true;
    public bool PersistAudit { get; init; } = true;
}
