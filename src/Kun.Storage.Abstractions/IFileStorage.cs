namespace Kun.Storage;

/// <summary>
/// Transport-agnostic object-storage abstraction used by Kun services to upload,
/// download, and manage blob artifacts (attachments, user uploads, exported files).
/// Implementations map the seven operations to a backing store (local filesystem,
/// Amazon S3, MinIO, Azure Blob, GCS).
/// </summary>
/// <remarks>
/// <para>
/// Object keys are relative, forward-slash-delimited paths (e.g. <c>tenants/42/avatar.png</c>).
/// They do not include a leading slash and do not identify the bucket / root — the
/// implementation resolves those from its configured options.
/// </para>
/// <para>
/// Implementations MUST force <c>Content-Disposition: attachment</c> on presigned
/// download URLs whose stored content type is in
/// <see cref="ActiveWebContentTypes.Value"/>, even if
/// <see cref="DownloadOptions.ForceDownload"/> is <c>false</c>. This defends against
/// cross-origin script execution when a tenant-uploaded HTML/SVG/XML blob is opened
/// inline by a browser.
/// </para>
/// </remarks>
public interface IFileStorage
{
    /// <summary>Uploads <paramref name="content"/> to <paramref name="objectKey"/>.</summary>
    /// <param name="objectKey">Storage object key (forward-slash-delimited path).</param>
    /// <param name="content">Readable stream with the payload. The stream is consumed fully.</param>
    /// <param name="contentType">MIME type of the payload (e.g. <c>image/png</c>).</param>
    /// <param name="constraints">Optional size and content-type policy; the store validates and rejects on violation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The final <see cref="ObjectMetadata"/> of the uploaded object.</returns>
    Task<ObjectMetadata> UploadAsync(
        string objectKey,
        Stream content,
        string contentType,
        UploadConstraints? constraints = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Produces a time-limited URL the caller can use to download the object directly
    /// from the backing store. The URL MUST include <c>Content-Disposition: attachment</c>
    /// when the stored content type is an active web content type (see remarks on
    /// <see cref="IFileStorage"/>).
    /// </summary>
    /// <param name="objectKey">Storage object key.</param>
    /// <param name="options">Optional download behavior (download-name override, custom response headers).</param>
    /// <param name="expiresIn">Validity window for the presigned URL.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<PresignedDownloadUrl> GetPresignedDownloadAsync(
        string objectKey,
        DownloadOptions? options = null,
        TimeSpan? expiresIn = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Produces a time-limited URL the caller can <c>PUT</c> content to. The returned
    /// record carries the exact HTTP headers the caller must send so server-side
    /// constraint enforcement works.
    /// </summary>
    /// <remarks>
    /// The PUT URL returned here cannot embed <see cref="UploadConstraints.MaxBytes"/> in
    /// the signature; consumers that need size enforcement at the S3 layer should use
    /// <see cref="GetPresignedPostUploadAsync"/> instead — its signed POST policy bakes
    /// <c>content-length-range</c> into the signature so S3 rejects oversized bodies with
    /// HTTP 400 before any bytes are persisted.
    /// </remarks>
    /// <param name="objectKey">Storage object key.</param>
    /// <param name="contentType">Declared content type for the upload.</param>
    /// <param name="constraints">Size / content-type policy enforced by the URL signature.</param>
    /// <param name="expiresIn">Validity window.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<PresignedUploadUrl> GetPresignedUploadAsync(
        string objectKey,
        string contentType,
        UploadConstraints? constraints = null,
        TimeSpan? expiresIn = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a presigned POST upload descriptor. The signed policy embeds
    /// <see cref="UploadConstraints.MaxBytes"/> when set, so the backing store (not the
    /// framework) rejects oversized uploads before any bytes are persisted. Allowed
    /// content types are encoded into the policy when
    /// <see cref="UploadConstraints.AllowedContentTypes"/> is supplied.
    /// </summary>
    /// <param name="objectKey">Target object key.</param>
    /// <param name="constraints">Optional upload constraints. When <c>null</c>, no size/type
    /// limits are baked into the policy.</param>
    /// <param name="expiresIn">Lifetime of the signed policy.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<PresignedPostUpload> GetPresignedPostUploadAsync(
        string objectKey,
        UploadConstraints? constraints = null,
        TimeSpan? expiresIn = null,
        CancellationToken cancellationToken = default);

    /// <summary>Returns <c>true</c> when an object with the given key exists.</summary>
    Task<bool> ExistsAsync(string objectKey, CancellationToken cancellationToken = default);

    /// <summary>Removes the object. Returns <c>true</c> if the object existed and was deleted; <c>false</c> otherwise.</summary>
    Task<bool> DeleteAsync(string objectKey, CancellationToken cancellationToken = default);

    /// <summary>Reads the metadata (size, content type, ETag, last-modified) of the object, or <c>null</c> if missing.</summary>
    Task<ObjectMetadata?> GetMetadataAsync(string objectKey, CancellationToken cancellationToken = default);

    /// <summary>Lists objects whose keys start with <paramref name="prefix"/>.</summary>
    /// <param name="prefix">Key prefix (e.g. <c>tenants/42/</c>). Pass <c>null</c> or empty for the root listing.</param>
    /// <param name="maxKeys">Upper bound on the number of objects returned in a single call. Implementations may clamp.</param>
    /// <param name="continuationToken">Opaque token returned from a previous call. Pass <c>null</c> for the first page.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ListObjectsResult> ListAsync(
        string? prefix = null,
        int maxKeys = 1000,
        string? continuationToken = null,
        CancellationToken cancellationToken = default);
}
