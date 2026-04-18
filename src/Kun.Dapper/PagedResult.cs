namespace Kun.Dapper;

/// <summary>A single page of query results with total-count metadata.</summary>
/// <typeparam name="T">The item type.</typeparam>
public sealed class PagedResult<T>
{
    /// <summary>Items on this page.</summary>
    public IReadOnlyList<T> Items { get; init; } = [];

    /// <summary>Total number of items across all pages.</summary>
    public int TotalCount { get; init; }

    /// <summary>The page request that produced this result.</summary>
    public required PageRequest Request { get; init; }

    /// <summary>Total number of pages.</summary>
    public int TotalPages =>
        TotalCount == 0 ? 0 : (int)Math.Ceiling((double)TotalCount / Request.PageSize);

    /// <summary>Whether a next page exists.</summary>
    public bool HasNextPage => Request.Page < TotalPages;

    /// <summary>Whether a previous page exists.</summary>
    public bool HasPreviousPage => Request.Page > 1;

    /// <summary>Creates an empty result for the given request.</summary>
    public static PagedResult<T> Empty(PageRequest request) =>
        new() { Request = request, TotalCount = 0 };
}
