namespace Kun.Storage.Abstractions;

/// <summary>
/// Lightweight description of an object returned by
/// <see cref="IFileStorage.ListObjectsAsync(string, CancellationToken)"/>.
/// </summary>
/// <param name="Path">Object key (logical path).</param>
/// <param name="Size">Object size in bytes.</param>
/// <param name="LastModified">UTC timestamp of the last successful write.</param>
/// <param name="ETag">Backend-assigned entity tag, if any.</param>
public sealed record ObjectSummary(
    string Path,
    long Size,
    DateTimeOffset LastModified,
    string? ETag = null);
