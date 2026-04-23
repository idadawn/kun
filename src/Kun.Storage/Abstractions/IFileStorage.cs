namespace Kun.Storage.Abstractions;

/// <summary>
/// Object-storage abstraction covering S3 / MinIO / local filesystem.
/// Method names and shapes match the Phase 1 contract in
/// <c>docs/neuroagent-phase1-gap.md §5.1</c> — do not rename or reduce
/// without a matching ADR + gap-audit update.
/// </summary>
/// <remarks>
/// <para>
/// Object keys are opaque strings. Implementations are free to translate
/// them into filesystem paths or S3 keys but must accept forward-slash
/// separators and must reject keys that begin with <c>/</c> or contain
/// <c>..</c> path-traversal segments.
/// </para>
/// <para>
/// Active-web content types (<c>text/html</c>, <c>text/xml</c>,
/// <c>application/xhtml+xml</c>, <c>image/svg+xml</c>,
/// <c>application/x-shockwave-flash</c>) are stored / served with
/// <c>Content-Disposition: attachment</c> forced so that user-uploaded
/// HTML cannot execute in the origin of the download host. See
/// <c>docs/neuroagent-phase1-gap.md §5.1 Security</c> for the rationale.
/// </para>
/// </remarks>
public interface IFileStorage
{
    /// <summary>Uploads <paramref name="content"/> as <paramref name="objectKey"/>.</summary>
    /// <param name="objectKey">The target object key.</param>
    /// <param name="content">The stream to upload; positioned at the first byte to read.</param>
    /// <param name="contentType">MIME type to persist as the object's <c>Content-Type</c>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Metadata for the uploaded object, including its final size and content type.</returns>
    Task<ObjectMetadata> PutObjectAsync(
        string objectKey,
        Stream content,
        string contentType,
        CancellationToken ct = default);

    /// <summary>
    /// Opens a read stream for <paramref name="objectKey"/>. The returned
    /// stream is owned by the caller and must be disposed.
    /// </summary>
    /// <exception cref="FileNotFoundException">The object does not exist.</exception>
    Task<Stream> GetObjectAsync(string objectKey, CancellationToken ct = default);

    /// <summary>
    /// Returns metadata for <paramref name="objectKey"/> without transferring the body.
    /// </summary>
    /// <returns><see langword="null"/> when the object does not exist.</returns>
    Task<ObjectMetadata?> HeadObjectAsync(string objectKey, CancellationToken ct = default);

    /// <summary>Deletes <paramref name="objectKey"/>. No-op when the object does not exist.</summary>
    Task DeleteObjectAsync(string objectKey, CancellationToken ct = default);

    /// <summary>Enumerates objects whose key starts with <paramref name="prefix"/>.</summary>
    /// <param name="prefix">Key prefix; <see langword="null"/> or empty enumerates the whole bucket.</param>
    /// <param name="ct">Cancellation token propagated through the async enumeration.</param>
    IAsyncEnumerable<ObjectMetadata> ListObjectsAsync(string? prefix = null, CancellationToken ct = default);

    /// <summary>
    /// Issues a short-lived URL that a client can <c>PUT</c> to in order
    /// to upload <paramref name="objectKey"/> directly.
    /// </summary>
    /// <param name="objectKey">Target object key.</param>
    /// <param name="expiresIn">Duration until the URL expires.</param>
    /// <param name="contentType">Expected MIME type; the client must send the same <c>Content-Type</c> header or S3 rejects the PUT. <see langword="null"/> disables the binding.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<PresignedUploadUrl> CreatePresignedUploadUrlAsync(
        string objectKey,
        TimeSpan expiresIn,
        string? contentType = null,
        CancellationToken ct = default);

    /// <summary>
    /// Issues a short-lived URL that a client can <c>GET</c> to download
    /// <paramref name="objectKey"/> directly from the storage backend.
    /// </summary>
    /// <param name="objectKey">Target object key.</param>
    /// <param name="expiresIn">Duration until the URL expires.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<PresignedDownloadUrl> CreatePresignedDownloadUrlAsync(
        string objectKey,
        TimeSpan expiresIn,
        CancellationToken ct = default);
}
