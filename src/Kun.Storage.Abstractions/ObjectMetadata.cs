namespace Kun.Storage;

/// <summary>Metadata describing a stored object.</summary>
/// <param name="Key">Object key (store-relative path).</param>
/// <param name="Size">Object size in bytes.</param>
/// <param name="ContentType">
/// Declared MIME type (e.g. <c>image/png</c>, <c>application/pdf</c>). <b>Nullable</b> —
/// <see cref="IFileStorage.ListAsync"/> results leave this <see langword="null"/> on
/// backends whose listing API omits content type (e.g. S3 <c>ListObjectsV2</c>).
/// <see cref="IFileStorage.GetMetadataAsync"/> and <see cref="IFileStorage.UploadAsync"/>
/// always populate it. Consumers iterating listings that need the content type should
/// follow up with <see cref="IFileStorage.GetMetadataAsync"/> on the items that matter.
/// </param>
/// <param name="ETag">Content-hash or version ETag supplied by the backing store.</param>
/// <param name="LastModified">Last modification timestamp reported by the store.</param>
public sealed record ObjectMetadata(
    string Key,
    long Size,
    string? ContentType,
    string? ETag,
    DateTimeOffset LastModified);
