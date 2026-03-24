using System.Security.Cryptography;
using System.Text;
using Identity.SelfService.Application;
using Identity.SelfService.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using BeeTracker.BuildingBlocks.Abstractions.Options;

namespace Identity.SelfService.Infrastructure;

public sealed class EfAdminAccountRepository(SelfServiceDbContext db) : IAdminAccountRepository
{
    public async Task<AdminAccountState?> GetAccountStateAsync(string userId, CancellationToken ct)
    {
        var entity = await db.AdminAccountStates.AsNoTracking()
            .FirstOrDefaultAsync(e => e.UserId == userId, ct);
        if (entity is null) return null;
        return Enum.TryParse<AdminAccountState>(entity.State, true, out var state) ? state : null;
    }

    public async Task SetAccountStateAsync(string userId, AdminAccountState state, CancellationToken ct)
    {
        var entity = await db.AdminAccountStates.FirstOrDefaultAsync(e => e.UserId == userId, ct);
        var now = DateTimeOffset.UtcNow;

        if (entity is null)
        {
            entity = new AdminAccountStateEntity
            {
                UserId = userId,
                State = state.ToString(),
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            db.AdminAccountStates.Add(entity);
        }
        else
        {
            entity.State = state.ToString();
            entity.UpdatedAtUtc = now;
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task<string?> GetUserIdByEmailAsync(string email, CancellationToken ct)
    {
        // This is handled through UserManager, but we keep the interface for completeness
        // In practice, callers use UserManager.FindByEmailAsync instead
        return null;
    }

    public async Task<DateTimeOffset?> GetCreatedAtUtcAsync(string userId, CancellationToken ct)
    {
        var entity = await db.AdminAccountStates.AsNoTracking()
            .FirstOrDefaultAsync(e => e.UserId == userId, ct);
        return entity?.CreatedAtUtc;
    }

    public async Task<DateTimeOffset?> GetLastLoginAtUtcAsync(string userId, CancellationToken ct)
    {
        var entity = await db.AdminAccountStates.AsNoTracking()
            .FirstOrDefaultAsync(e => e.UserId == userId, ct);
        return entity?.LastLoginAtUtc;
    }

    public async Task RecordLoginAsync(string userId, DateTimeOffset now, CancellationToken ct)
    {
        var entity = await db.AdminAccountStates.FirstOrDefaultAsync(e => e.UserId == userId, ct);
        if (entity is not null)
        {
            entity.LastLoginAtUtc = now;
            entity.UpdatedAtUtc = now;
            await db.SaveChangesAsync(ct);
        }
    }
}

public sealed class EfVerificationTokenRepository(SelfServiceDbContext db) : IVerificationTokenRepository
{
    public async Task CreateAsync(VerificationToken token, CancellationToken ct)
    {
        var entity = new VerificationTokenEntity
        {
            Id = token.Id,
            UserId = token.UserId,
            Purpose = token.Purpose.ToString(),
            TokenHash = token.TokenHash,
            ExpiresAtUtc = token.ExpiresAtUtc,
            CreatedAtUtc = token.CreatedAtUtc,
            ConsumedAtUtc = token.ConsumedAtUtc,
            RevokedAtUtc = token.RevokedAtUtc
        };
        db.VerificationTokens.Add(entity);
        await db.SaveChangesAsync(ct);
    }

    public async Task<VerificationToken?> FindValidByHashAsync(string tokenHash, VerificationTokenPurpose purpose, CancellationToken ct)
    {
        var purposeStr = purpose.ToString();
        var now = DateTimeOffset.UtcNow;

        var entity = await db.VerificationTokens.AsNoTracking()
            .Where(e => e.TokenHash == tokenHash
                && e.Purpose == purposeStr
                && e.ConsumedAtUtc == null
                && e.RevokedAtUtc == null
                && e.ExpiresAtUtc > now)
            .FirstOrDefaultAsync(ct);

        if (entity is null) return null;

        return MapToDomain(entity);
    }

    public async Task RevokeAllForUserAndPurposeAsync(string userId, VerificationTokenPurpose purpose, DateTimeOffset now, CancellationToken ct)
    {
        var purposeStr = purpose.ToString();
        var tokens = await db.VerificationTokens
            .Where(e => e.UserId == userId
                && e.Purpose == purposeStr
                && e.ConsumedAtUtc == null
                && e.RevokedAtUtc == null)
            .ToListAsync(ct);

        foreach (var token in tokens)
        {
            token.RevokedAtUtc = now;
        }

        if (tokens.Count > 0)
            await db.SaveChangesAsync(ct);
    }

    public async Task ConsumeAsync(Guid tokenId, DateTimeOffset now, CancellationToken ct)
    {
        var entity = await db.VerificationTokens.FirstOrDefaultAsync(e => e.Id == tokenId, ct);
        if (entity is not null)
        {
            entity.ConsumedAtUtc = now;
            await db.SaveChangesAsync(ct);
        }
    }

    private static VerificationToken MapToDomain(VerificationTokenEntity entity)
    {
        var purpose = Enum.Parse<VerificationTokenPurpose>(entity.Purpose, true);
        var token = VerificationToken.Create(entity.UserId, purpose, entity.TokenHash, entity.ExpiresAtUtc);
        // Reconstitute state via reflection-free approach using the static Create and then setting consumed/revoked
        // Since Create returns a fresh token, we need to reconstruct the full state
        return VerificationToken.Reconstitute(
            entity.Id, entity.UserId, purpose, entity.TokenHash,
            entity.ExpiresAtUtc, entity.CreatedAtUtc, entity.ConsumedAtUtc, entity.RevokedAtUtc);
    }
}

public sealed class Sha256TokenHasher : ITokenHasher
{
    public string Hash(string rawToken)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToBase64String(bytes);
    }

    public string GenerateRawToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_").TrimEnd('=');
    }
}

public static class SelfServiceInfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddSelfServiceInfrastructure(this IServiceCollection services)
    {
        services.AddDbContext<SelfServiceDbContext>((sp, options) =>
        {
            var postgresOptions = sp.GetRequiredService<IOptions<PostgresOptions>>().Value;
            options.UseNpgsql(
                postgresOptions.ConnectionString,
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", SelfServiceDbContext.SchemaName));
        });

        services.AddScoped<IAdminAccountRepository, EfAdminAccountRepository>();
        services.AddScoped<IVerificationTokenRepository, EfVerificationTokenRepository>();
        services.AddSingleton<ITokenHasher, Sha256TokenHasher>();
        services.AddScoped<IRbacService, RbacService>();
        services.AddScoped<RbacSeedService>();

        return services;
    }
}
