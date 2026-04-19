namespace Kun.Dapper;

/// <summary>Pagination parameters for a read query.</summary>
/// <param name="Page">1-based page number.</param>
/// <param name="PageSize">Number of items per page.</param>
public sealed record PageRequest(int Page = 1, int PageSize = 20)
{
    /// <summary>Zero-based row offset for SQL <c>OFFSET … FETCH</c>.</summary>
    public int Offset => (Page - 1) * PageSize;
}
