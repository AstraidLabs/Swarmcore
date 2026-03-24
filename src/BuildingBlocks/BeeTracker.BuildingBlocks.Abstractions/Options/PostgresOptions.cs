namespace BeeTracker.BuildingBlocks.Abstractions.Options;

public sealed class PostgresOptions
{
    public const string SectionName = "BeeTracker:Postgres";

    public string ConnectionString { get; init; } = string.Empty;
}
