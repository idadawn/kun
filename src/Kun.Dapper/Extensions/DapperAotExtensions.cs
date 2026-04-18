using System.Data;
using Dapper;

namespace Kun.Dapper.Extensions;

/// <summary>
/// Pagination helpers for Dapper query handlers.
/// </summary>
/// <remarks>
/// These helpers compose count + page queries into a single <see cref="PagedResult{T}"/>.
/// Callers are expected to annotate their query methods with <c>[DapperAot]</c> so that
/// Dapper.AOT generates the reader code at build time. The helpers themselves only call
/// the standard Dapper <see cref="SqlMapper"/> extensions and introduce no runtime reflection.
/// </remarks>
public static class DapperAotExtensions
{
    /// <summary>
    /// Executes a count query and a page query in sequence and wraps the results in a
    /// <see cref="PagedResult{T}"/>.
    /// </summary>
    /// <typeparam name="T">The item type returned by the page query.</typeparam>
    /// <param name="connection">An open <see cref="IDbConnection"/>.</param>
    /// <param name="countSql">SQL that returns a single <c>int</c> total-row count.</param>
    /// <param name="pageSql">
    /// SQL that returns the page of rows. May reference <c>@Offset</c> and <c>@PageSize</c>
    /// — these are automatically merged into <paramref name="param"/> if not already present.
    /// </param>
    /// <param name="request">The page request containing page number and size.</param>
    /// <param name="param">
    /// Optional parameter bag (anonymous object or <see cref="DynamicParameters"/>).
    /// <c>@Offset</c> and <c>@PageSize</c> are added automatically.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="PagedResult{T}"/> with the total count and current page of items.</returns>
    public static async Task<PagedResult<T>> QueryPageAsync<T>(
        this IDbConnection connection,
        string countSql,
        string pageSql,
        PageRequest request,
        object? param = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentException.ThrowIfNullOrEmpty(countSql);
        ArgumentException.ThrowIfNullOrEmpty(pageSql);
        ArgumentNullException.ThrowIfNull(request);

        // Build a DynamicParameters bag that includes Offset + PageSize plus any caller params.
        var dp = new DynamicParameters(param);
        dp.Add("Offset", request.Offset);
        dp.Add("PageSize", request.PageSize);

        var commandDefinition = new CommandDefinition(countSql, dp, cancellationToken: cancellationToken);
        int totalCount = await connection.ExecuteScalarAsync<int>(commandDefinition).ConfigureAwait(false);

        if (totalCount == 0)
        {
            return PagedResult<T>.Empty(request);
        }

        var pageCommand = new CommandDefinition(pageSql, dp, cancellationToken: cancellationToken);
        IEnumerable<T> items = await connection.QueryAsync<T>(pageCommand).ConfigureAwait(false);

        return new PagedResult<T>
        {
            Items = items.ToList(),
            TotalCount = totalCount,
            Request = request,
        };
    }
}
