namespace Kun.Storage.Abstractions;

/// <summary>
/// Abstraction for object-storage operations. Supports multiple backends
/// (local filesystem, S3, MinIO) behind a uniform contract.
/// </summary>
/// <remarks>
/// Implementations must enforce <see cref="ContentDispositionPolicy"/> on uploads
/// and presigned download URLs, so that browsers never render user-uploaded
/// active-web types (HTML, SVG, XML, JavaScript, WASM) inline.
/// </remarks>
[Obsolete("请引用 Kun.Storage.Abstractions 包里的 Kun.Storage.IFileStorage；旧包仅保留兼容。", DiagnosticId = "KUNLEGACY001")]
public interface IFileStorage
{
    /// <summary>Uploads an object to <paramref name="path"/>.</summary>
    /// <param name="path">Object key (logical path, forward-slash separated).</param>
    /// <param name="content">Object bytes. The stream is read from its current position to end.</param>
    /// <param name="options">Content type, disposition, cache-control, and user metadata.</param>
    /// <param name="ct">Cancellation token.</param>
    Task PutObjectAsync(
        string path,
        Stream content,
        PutObjectOptions options,
        CancellationToken ct = default);

    /// <summary>Fetches an object by path.</summary>
    /// <returns>Content stream and metadata. Dispose to release the underlying stream.</returns>
    /// <exception cref="FileNotFoundException">Thrown if the object does not exist.</exception>
    Task<ObjectDownload> GetObjectAsync(string path, CancellationToken ct = default);

    /// <summary>Fetches metadata for an object without downloading its body.</summary>
    /// <returns>Metadata, or <see langword="null"/> if the object does not exist.</returns>
    Task<ObjectMetadata?> HeadObjectAsync(string path, CancellationToken ct = default);

    /// <summary>Deletes an object. No-op if the object does not exist.</summary>
    Task DeleteObjectAsync(string path, CancellationToken ct = default);

    /// <summary>Enumerates objects whose path starts with <paramref name="prefix"/>.</summary>
    /// <param name="prefix">Logical path prefix. Use empty string to list the whole bucket/root.</param>
    /// <param name="ct">Cancellation token.</param>
    IAsyncEnumerable<ObjectSummary> ListObjectsAsync(
        string prefix,
        CancellationToken ct = default);

    /// <summary>Generates a short-lived URL that the client can use to upload an object directly.</summary>
    /// <remarks>
    /// The returned URL carries <see cref="PresignedUploadUrl.RequiredHeaders"/> that the caller
    /// MUST forward to the storage backend so the pre-signature matches.
    /// </remarks>
    Task<PresignedUploadUrl> CreatePresignedUploadUrlAsync(
        string path,
        PresignedUploadRequest request,
        CancellationToken ct = default);

    /// <summary>Generates a short-lived URL that the client can use to download an object directly.</summary>
    /// <remarks>
    /// If the stored object's content-type is in <see cref="ContentDispositionPolicy"/>'s
    /// unsafe-for-inline-display list and the request did not already specify
    /// <see cref="PresignedDownloadRequest.ResponseContentDisposition"/>, implementations
    /// MUST override the response disposition to force <c>attachment</c>.
    /// </remarks>
    Task<PresignedDownloadUrl> CreatePresignedDownloadUrlAsync(
        string path,
        PresignedDownloadRequest request,
        CancellationToken ct = default);
}
