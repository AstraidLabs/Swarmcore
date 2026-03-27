using Microsoft.EntityFrameworkCore;

namespace BeeTracker.BuildingBlocks.Infrastructure.Data;

public static class QueryPagingExtensions
{
    public static async Task<(IReadOnlyList<T> Items, int TotalCount)> ToPageAsync<T>(
        this IQueryable<T> query,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items.AsReadOnly(), totalCount);
    }
}
