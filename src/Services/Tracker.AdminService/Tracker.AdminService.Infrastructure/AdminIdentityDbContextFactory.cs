using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Tracker.AdminService.Infrastructure;

public sealed class AdminIdentityDbContextFactory : IDesignTimeDbContextFactory<AdminIdentityDbContext>
{
    public AdminIdentityDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AdminIdentityDbContext>();
        optionsBuilder.UseNpgsql(
            Environment.GetEnvironmentVariable("SWARMCORE_POSTGRES_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=swarmcore;Username=swarmcore;Password=swarmcore",
            npgsqlOptions => npgsqlOptions.MigrationsHistoryTable("__EFMigrationsHistory", AdminIdentityDbContext.SchemaName));
        optionsBuilder.UseOpenIddict();
        return new AdminIdentityDbContext(optionsBuilder.Options);
    }
}
