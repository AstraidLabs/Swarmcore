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

    public string NormalizedFilter =>
        string.IsNullOrWhiteSpace(Filter) ? "all" : Filter.Trim();
}

public sealed record GridSortTerm(string Field, GridSortDirection Direction);

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
