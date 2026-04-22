namespace Kun.Storage.Abstractions;

/// <summary>
/// Describes a single object in storage. Returned by
/// <see cref="IFileStorage.PutObjectAsync"/>,
/// <see cref="IFileStorage.HeadObjectAsync"/>, and
/// <see cref="IFileStorage.ListObjectsAsync"/>.
/// </summary>
/// <param name="ObjectKey">Key under which the object is stored.</param>
/// <param name="Size">Size of the object body in bytes.</param>
/// <param name="ContentType">MIME type persisted with the object; <see langword="null"/> when unknown (e.g. local filesystem without sidecar metadata).</param>
/// <param name="Sha256">Lower-case hex SHA-256 of the object body; <see langword="null"/> when the backend does not expose a content hash.</param>
/// <param name="LastModified">UTC timestamp of the last successful write.</param>
public sealed record ObjectMetadata(
    string ObjectKey,
    long Size,
    string? ContentType,
    string? Sha256,
    DateTimeOffset LastModified);
