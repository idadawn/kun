using System.Data;
using Kun.Dapper;
using Npgsql;

namespace Kun.Sample.Catalog.Infrastructure;

/// <summary>
/// Npgsql-backed <see cref="IReadDbConnectionFactory"/> for the read side of Catalog.
/// Opens a fresh connection per call; the caller disposes it.
/// </summary>
public sealed class NpgsqlReadDbConnectionFactory(ReadDbOptions options) : IReadDbConnectionFactory
{
    /// <inheritdoc/>
    public async Task<IDbConnection> OpenAsync(CancellationToken cancellationToken = default)
    {
        var connection = new NpgsqlConnection(options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }
}
