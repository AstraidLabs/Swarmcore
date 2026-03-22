namespace Swarmcore.BuildingBlocks.Abstractions.Options;

public sealed class PostgresOptions
{
    public const string SectionName = "Swarmcore:Postgres";

    public string ConnectionString { get; init; } = string.Empty;
}
