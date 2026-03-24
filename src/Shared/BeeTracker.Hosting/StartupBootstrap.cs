using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using StackExchange.Redis;
using BeeTracker.BuildingBlocks.Abstractions.Options;

namespace BeeTracker.Hosting;

public static class StartupBootstrap
{
    public static async Task WaitForPostgresAsync(IServiceProvider serviceProvider, string owner, CancellationToken cancellationToken)
    {
        var options = serviceProvider.GetRequiredService<IOptions<StartupBootstrapOptions>>().Value;
        var postgresOptions = serviceProvider.GetRequiredService<IOptions<PostgresOptions>>().Value;
        var logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger($"{owner}.Bootstrap");

        await RetryAsync(
            owner,
            "PostgreSQL",
            logger,
            options,
            async token =>
            {
                await using var connection = new NpgsqlConnection(postgresOptions.ConnectionString);
                await connection.OpenAsync(token);
                await using var command = new NpgsqlCommand("select 1", connection);
                await command.ExecuteScalarAsync(token);
            },
            cancellationToken);
    }

    public static async Task WaitForRedisAsync(IServiceProvider serviceProvider, string owner, CancellationToken cancellationToken)
    {
        var options = serviceProvider.GetRequiredService<IOptions<StartupBootstrapOptions>>().Value;
        var redisOptions = serviceProvider.GetRequiredService<IOptions<RedisOptions>>().Value;
        var logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger($"{owner}.Bootstrap");

        await RetryAsync(
            owner,
            "Redis",
            logger,
            options,
            async token =>
            {
                await using var multiplexer = await ConnectionMultiplexer.ConnectAsync(redisOptions.Configuration);
                await multiplexer.GetDatabase().PingAsync();
            },
            cancellationToken);
    }

    public static Task MigrateDbContextAsync<TContext>(IServiceProvider serviceProvider, CancellationToken cancellationToken)
        where TContext : DbContext
    {
        var dbContext = serviceProvider.GetRequiredService<TContext>();
        return dbContext.Database.MigrateAsync(cancellationToken);
    }

    private static async Task RetryAsync(
        string owner,
        string dependencyName,
        ILogger logger,
        StartupBootstrapOptions options,
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken)
    {
        Exception? lastException = null;

        for (var attempt = 1; attempt <= options.MaxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await action(cancellationToken);
                logger.LogInformation("{Owner} bootstrap connected to {DependencyName} on attempt {Attempt}.", owner, dependencyName, attempt);
                return;
            }
            catch (Exception exception) when (attempt < options.MaxAttempts)
            {
                lastException = exception;
                logger.LogWarning(exception, "{Owner} bootstrap could not connect to {DependencyName} on attempt {Attempt}/{MaxAttempts}. Retrying in {DelaySeconds}s.", owner, dependencyName, attempt, options.MaxAttempts, options.RetryDelaySeconds);
                await Task.Delay(TimeSpan.FromSeconds(options.RetryDelaySeconds), cancellationToken);
            }
            catch (Exception exception)
            {
                lastException = exception;
                break;
            }
        }

        throw new InvalidOperationException($"{owner} bootstrap failed to connect to {dependencyName} after {options.MaxAttempts} attempts.", lastException);
    }
}
