using BeeTracker.BuildingBlocks.Application.Queries;

namespace Tracker.AdminService.Api;

internal static class GridQueryHttp
{
    public static GridQuery Bind(string? search, string? filter, string? sort, int page, int pageSize)
        => new(search, filter, sort, page, pageSize);

    public static GridQuery Bind(string? search, string? filter, string? sort, int page, int pageSize, params string[] allowedFilters)
        => new(search, GridFilterParser.Normalize(filter, allowedFilters), sort, page, pageSize);

    public static GridQuery Bind(string? search, string? filter, string? sort, int page, int pageSize, GridCatalogProfile profile)
        => new(search, profile.NormalizeFilter(filter), sort, page, pageSize);

    public static TFilter ParseFilter<TFilter>(string? filter)
        where TFilter : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(filter))
            return ParseAllOrDefault<TFilter>();

        if (Enum.TryParse<TFilter>(filter.Trim(), true, out var parsed))
            return parsed;

        return ParseAllOrDefault<TFilter>();
    }

    private static TFilter ParseAllOrDefault<TFilter>()
        where TFilter : struct, Enum
        => Enum.TryParse<TFilter>("All", true, out var parsed) ? parsed : default;
}
