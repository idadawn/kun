namespace Kun.Storage;

/// <summary>Metadata describing a stored object.</summary>
/// <param name="Key">Object key (store-relative path).</param>
/// <param name="Size">Object size in bytes.</param>
/// <param name="ContentType">Declared MIME type (e.g. <c>image/png</c>, <c>application/pdf</c>).</param>
/// <param name="ETag">Content-hash or version ETag supplied by the backing store.</param>
/// <param name="LastModified">Last modification timestamp reported by the store.</param>
public sealed record ObjectMetadata(
    string Key,
    long Size,
    string ContentType,
    string? ETag,
    DateTimeOffset LastModified);
