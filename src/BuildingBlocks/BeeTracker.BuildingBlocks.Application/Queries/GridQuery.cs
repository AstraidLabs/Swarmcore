namespace BeeTracker.BuildingBlocks.Application.Queries;

public sealed record GridQuery(
    string? Search,
    string? Filter,
    string? Sort,
    int Page,
    int PageSize)
{
    public GridQuery Normalize(int defaultPageSize = 25, int maxPageSize = 100)
    {
        var normalizedPage = Page <= 0 ? 1 : Page;
        var requestedPageSize = PageSize <= 0 ? defaultPageSize : PageSize;
        var normalizedPageSize = Math.Clamp(requestedPageSize, 1, maxPageSize);
        return this with { Page = normalizedPage, PageSize = normalizedPageSize };
    }

    public string? NormalizedSearch =>
        string.IsNullOrWhiteSpace(Search) ? null : Search.Trim();

    public bool HasSearch => NormalizedSearch is { Length: > 0 };

    public string? ToSqlLikePattern()
    {
        if (!HasSearch)
            return null;

        return $"%{EscapeLikePattern(NormalizedSearch!)}%";
    }

    public string NormalizedFilter =>
        string.IsNullOrWhiteSpace(Filter) ? "all" : Filter.Trim();

    private static string EscapeLikePattern(string value)
        => value.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_");
}

public sealed record GridSortTerm(string Field, GridSortDirection Direction);

public sealed record GridCatalogProfile(
    IReadOnlyList<string> AllowedFilters,
    IReadOnlyList<string> AllowedSortFields,
    IReadOnlyList<GridSortTerm> DefaultSortTerms);

public enum GridSortDirection
{
    Asc,
    Desc
}

public static class GridQuerySortParser
{
    public static IReadOnlyList<GridSortTerm> Parse(string? sort, params GridSortTerm[] defaultTerms)
    {
        if (string.IsNullOrWhiteSpace(sort))
            return defaultTerms;

        var terms = sort
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ParseTerm)
            .Where(term => term is not null)
            .Cast<GridSortTerm>()
            .ToList();

        return terms.Count == 0 ? defaultTerms : terms;
    }

    private static GridSortTerm? ParseTerm(string? rawTerm)
    {
        if (string.IsNullOrWhiteSpace(rawTerm))
            return null;

        var tokens = rawTerm.Split(':', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0 || string.IsNullOrWhiteSpace(tokens[0]))
            return null;

        var field = tokens[0].Trim().ToLowerInvariant();
        var direction = tokens.Length > 1 && tokens[1].Equals("desc", StringComparison.OrdinalIgnoreCase)
            ? GridSortDirection.Desc
            : GridSortDirection.Asc;

        return new GridSortTerm(field, direction);
    }
}

public static class GridQuerySortWhitelist
{
    public static IReadOnlyList<GridSortTerm> Parse(
        string? sort,
        IEnumerable<string> allowedFields,
        params GridSortTerm[] defaultTerms)
    {
        var normalizedAllowed = new HashSet<string>(
            allowedFields.Select(static field => field.Trim().ToLowerInvariant()),
            StringComparer.OrdinalIgnoreCase);

        if (normalizedAllowed.Count == 0)
            return defaultTerms;

        var parsedTerms = GridQuerySortParser.Parse(sort, defaultTerms);
        var filteredTerms = parsedTerms
            .Where(term => normalizedAllowed.Contains(term.Field))
            .ToList();

        return filteredTerms.Count == 0 ? defaultTerms : filteredTerms;
    }
}

public static class GridCatalogProfileExtensions
{
    public static string NormalizeFilter(this GridCatalogProfile profile, string? filter)
        => GridFilterParser.Normalize(filter, profile.AllowedFilters.ToArray());

    public static IReadOnlyList<GridSortTerm> ParseSort(this GridCatalogProfile profile, string? sort)
        => GridQuerySortWhitelist.Parse(sort, profile.AllowedSortFields, profile.DefaultSortTerms.ToArray());
}

public static class GridFilterParser
{
    public static string Normalize(string? filter, params string[] allowedValues)
    {
        if (allowedValues is null || allowedValues.Length == 0)
            return string.IsNullOrWhiteSpace(filter) ? "all" : filter.Trim().ToLowerInvariant();

        var normalizedFilter = string.IsNullOrWhiteSpace(filter) ? "all" : filter.Trim().ToLowerInvariant();
        if (normalizedFilter == "all")
            return normalizedFilter;

        return allowedValues.Any(value => value.Equals(normalizedFilter, StringComparison.OrdinalIgnoreCase))
            ? normalizedFilter
            : "all";
    }
}
