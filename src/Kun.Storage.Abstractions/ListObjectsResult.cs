namespace Kun.Storage;

/// <summary>A page of objects returned by <see cref="IFileStorage.ListAsync"/>.</summary>
/// <param name="Objects">The objects in this page.</param>
/// <param name="ContinuationToken">
/// Opaque token to pass to the next <c>ListAsync</c> call to fetch the next page, or
/// <c>null</c> when the current page is the last.
/// </param>
public sealed record ListObjectsResult(
    IReadOnlyList<ObjectMetadata> Objects,
    string? ContinuationToken);
