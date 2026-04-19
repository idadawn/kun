using System.Data;

namespace Kun.Dapper;

/// <summary>Creates read-only database connections for query handlers.</summary>
public interface IReadDbConnectionFactory
{
    /// <summary>Opens and returns a new read connection.</summary>
    /// <remarks>The caller is responsible for disposing the returned connection.</remarks>
    Task<IDbConnection> OpenAsync(CancellationToken cancellationToken = default);
}
