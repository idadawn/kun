using System.Data;
using Npgsql;

namespace Kun.Dapper.Npgsql;

/// <summary>
/// Npgsql-backed <see cref="IReadDbConnectionFactory"/>.
/// Opens a fresh connection per call; the caller is responsible for disposing it.
/// </summary>
public sealed class NpgsqlReadDbConnectionFactory : IReadDbConnectionFactory
{
    private readonly ReadDbOptions _options;

    /// <summary>Initializes a new <see cref="NpgsqlReadDbConnectionFactory"/>.</summary>
    public NpgsqlReadDbConnectionFactory(ReadDbOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    /// <inheritdoc/>
    public async Task<IDbConnection> OpenAsync(CancellationToken cancellationToken = default)
    {
        var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }
}
