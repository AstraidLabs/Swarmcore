using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Tracker.ConfigurationService.Infrastructure;

public sealed class TrackerConfigurationDbContextFactory : IDesignTimeDbContextFactory<TrackerConfigurationDbContext>
{
    public TrackerConfigurationDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<TrackerConfigurationDbContext>();
        optionsBuilder.UseNpgsql(GetConnectionString());
        return new TrackerConfigurationDbContext(optionsBuilder.Options);
    }

    private static string GetConnectionString()
        => Environment.GetEnvironmentVariable("BEETRACKER_POSTGRES_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=beetracker;Username=beetracker;Password=beetracker";
}
