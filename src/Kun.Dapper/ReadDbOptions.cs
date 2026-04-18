namespace Kun.Dapper;

/// <summary>Configuration for the Dapper read-side connection factory.</summary>
public sealed class ReadDbOptions
{
    /// <summary>ADO.NET connection string for the read replica.</summary>
    public required string ConnectionString { get; init; }
}
