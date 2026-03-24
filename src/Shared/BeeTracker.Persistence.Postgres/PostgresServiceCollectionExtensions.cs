using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using BeeTracker.BuildingBlocks.Abstractions.Options;

namespace BeeTracker.Persistence.Postgres;

public interface IPostgresConnectionFactory
{
    ValueTask<NpgsqlConnection> OpenConnectionAsync(CancellationToken cancellationToken);
}

public sealed class PostgresConnectionFactory(IOptions<PostgresOptions> options) : IPostgresConnectionFactory
{
    public async ValueTask<NpgsqlConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(options.Value.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }
}

public static class PostgresServiceCollectionExtensions
{
    public static IServiceCollection AddPostgresFoundation(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<PostgresOptions>()
            .Bind(configuration.GetSection(PostgresOptions.SectionName))
            .Validate(static options => !string.IsNullOrWhiteSpace(options.ConnectionString), "Postgres connection string is required.")
            .ValidateOnStart();

        services.AddSingleton<IPostgresConnectionFactory, PostgresConnectionFactory>();
        return services;
    }
}
