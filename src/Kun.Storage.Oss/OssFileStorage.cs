using Aliyun.OSS;
using Aliyun.OSS.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OssObjectMetadata = Aliyun.OSS.ObjectMetadata;

namespace Kun.Storage.Oss;

/// <summary>
/// Alibaba Cloud OSS implementation of <see cref="IFileStorage"/> using the native
/// <c>Aliyun.OSS.SDK.NetCore</c> client.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why native and not the S3-compatible path:</b> OSS exposes an S3-compatible surface, but
/// its presigned URLs are validated with OSS's own signature algorithm — not AWS SigV4. An AWS
/// SDK client pointed at an OSS endpoint emits SigV4-signed URLs that OSS rejects, so presigned
/// upload/download (the core of this contract) does not work over <c>Kun.Storage.S3</c>. This
/// type therefore wraps the native SDK and is the supported way to target OSS.
/// </para>
/// <para>
/// <b>Security guarantee (M1):</b> <see cref="GetPresignedDownloadAsync"/> ALWAYS sets
/// <c>response-content-disposition=attachment</c> on the signed URL when the stored object's
/// content type is one of <see cref="ActiveWebContentTypes.Value"/>, regardless of
/// <see cref="DownloadOptions.ForceDownload"/>.
/// </para>
/// <para>
/// <b>Async posture:</b> the Aliyun SDK exposes only synchronous and APM (Begin/End) operations,
/// no TAP. Network calls are offloaded with <see cref="Task.Run{TResult}(Func{TResult}, CancellationToken)"/>
/// so callers keep an async surface; the token cancels queueing, not an in-flight OSS request.
/// Presigned-URL generation is pure local signing (no I/O) and completes synchronously.
/// </para>
/// </remarks>
public sealed partial class OssFileStorage : IFileStorage
{
    private readonly IOss _oss;
    private readonly OssStorageOptions _options;
    private readonly ILogger<OssFileStorage> _logger;
    private readonly TimeProvider _timeProvider;

    /// <summary>Initializes a new instance of the <see cref="OssFileStorage"/> class.</summary>
    public OssFileStorage(
        IOss oss,
        IOptions<OssStorageOptions> options,
        ILogger<OssFileStorage> logger,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(oss);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _oss = oss;
        _options = options.Value;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;

        if (string.IsNullOrWhiteSpace(_options.BucketName))
        {
            throw new InvalidOperationException("OssStorageOptions.BucketName must be set before use.");
        }
    }

    /// <inheritdoc/>
    public async Task<ObjectMetadata> UploadAsync(
        string objectKey,
        Stream content,
        string contentType,
        UploadConstraints? constraints = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
        ValidateContentType(contentType, constraints);

        var meta = new OssObjectMetadata { ContentType = contentType };
        await Task.Run(() => _oss.PutObject(_options.BucketName, objectKey, content, meta), cancellationToken)
            .ConfigureAwait(false);
        Log.Uploaded(_logger, objectKey, _options.BucketName);

        var head = await Task.Run(() => _oss.GetObjectMetadata(_options.BucketName, objectKey), cancellationToken)
            .ConfigureAwait(false);

        if (constraints?.MaxBytes is { } max && head.ContentLength > max)
        {
            await Task.Run(() => _oss.DeleteObject(_options.BucketName, objectKey), cancellationToken)
                .ConfigureAwait(false);
            throw new InvalidOperationException(
                $"Upload size {head.ContentLength} bytes exceeds MaxBytes={max}.");
        }

        return ToMetadata(objectKey, head);
    }

    /// <inheritdoc/>
    public async Task<PresignedDownloadUrl> GetPresignedDownloadAsync(
        string objectKey,
        DownloadOptions? options = null,
        TimeSpan? expiresIn = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);

        var lifetime = expiresIn ?? _options.DefaultPresignedUrlLifetime;
        var expiresAt = _timeProvider.GetUtcNow().Add(lifetime);

        // Peek at the stored content type to decide whether to force attachment.
        var head = await Task.Run(() => _oss.GetObjectMetadata(_options.BucketName, objectKey), cancellationToken)
            .ConfigureAwait(false);

        var contentType = head.ContentType;
        var active = ActiveWebContentTypes.Contains(contentType);
        var forceAttachment = active || (options?.ForceDownload ?? false);

        var request = new GeneratePresignedUriRequest(_options.BucketName, objectKey, SignHttpMethod.Get)
        {
            Expiration = expiresAt.UtcDateTime,
        };

        // Apply caller overrides first so the force-attachment clamp below cannot be shadowed.
        var responseHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (options?.ResponseHeaders is { Count: > 0 } custom)
        {
            foreach (var (k, v) in custom)
            {
                responseHeaders[k] = v;
            }
        }

        if (forceAttachment)
        {
            var fileName = SanitizeFileName(options?.DownloadFileName ?? GetLeafName(objectKey));
            // No space after ';' (and SanitizeFileName strips spaces from fileName): this value
            // is emitted into the presigned URL's response-content-disposition query param, and
            // the Aliyun OSS SDK does NOT URL-encode spaces there — a raw space produces a
            // malformed download URL that clients reject ("URL rejected: malformed input").
            // RFC 6266 permits omitting the optional whitespace around ';'.
            var disposition = $"attachment;filename=\"{fileName}\"";
            request.ResponseHeaders.ContentDisposition = disposition;
            responseHeaders["Content-Disposition"] = disposition;
        }

        // GeneratePresignedUri is pure local signing (no network I/O) — call it synchronously.
        var url = _oss.GeneratePresignedUri(request);

        return new PresignedDownloadUrl(url, responseHeaders, expiresAt, active);
    }

    /// <inheritdoc/>
    public Task<PresignedUploadUrl> GetPresignedUploadAsync(
        string objectKey,
        string contentType,
        UploadConstraints? constraints = null,
        TimeSpan? expiresIn = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
        ValidateContentType(contentType, constraints);

        var lifetime = expiresIn ?? _options.DefaultPresignedUrlLifetime;
        var expiresAt = _timeProvider.GetUtcNow().Add(lifetime);

        var request = new GeneratePresignedUriRequest(_options.BucketName, objectKey, SignHttpMethod.Put)
        {
            Expiration = expiresAt.UtcDateTime,
            ContentType = contentType,
        };

        var url = _oss.GeneratePresignedUri(request);

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Content-Type"] = contentType,
        };

        return Task.FromResult(new PresignedUploadUrl(url, headers, expiresAt));
    }

    /// <inheritdoc/>
    public Task<PresignedPostUpload> GetPresignedPostUploadAsync(
        string objectKey,
        UploadConstraints? constraints = null,
        TimeSpan? expiresIn = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);

        // OSS supports browser POST-policy uploads, but with its own policy document and an
        // HMAC-SHA1 signature distinct from S3's. a consumer upload path uses the PUT presigned URL
        // (GetPresignedUploadAsync), so a POST-policy variant is deferred to Phase 2 rather than
        // shipped unverified: its signature cannot be validated without a live OSS endpoint, and
        // a wrong-but-plausible implementation would be worse than an explicit gap.
        throw new NotSupportedException(
            "OSS presigned POST-policy upload is not implemented. Use GetPresignedUploadAsync " +
            "(PUT) instead. Tracked for Phase 2.");
    }

    /// <inheritdoc/>
    public async Task<bool> ExistsAsync(string objectKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);
        return await Task.Run(() => _oss.DoesObjectExist(_options.BucketName, objectKey), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<Stream> OpenReadAsync(string objectKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);
        try
        {
            var obj = await Task.Run(() => _oss.GetObject(_options.BucketName, objectKey), cancellationToken)
                .ConfigureAwait(false);
            return obj.Content;
        }
        catch (OssException ex) when (ex.ErrorCode == "NoSuchKey")
        {
            throw new FileNotFoundException($"Object '{objectKey}' not found.", objectKey);
        }
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteAsync(string objectKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);
        if (!await ExistsAsync(objectKey, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        await Task.Run(() => _oss.DeleteObject(_options.BucketName, objectKey), cancellationToken)
            .ConfigureAwait(false);
        Log.Deleted(_logger, objectKey, _options.BucketName);
        return true;
    }

    /// <inheritdoc/>
    public async Task<ObjectMetadata?> GetMetadataAsync(string objectKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);
        try
        {
            var head = await Task.Run(() => _oss.GetObjectMetadata(_options.BucketName, objectKey), cancellationToken)
                .ConfigureAwait(false);
            return ToMetadata(objectKey, head);
        }
        catch (OssException ex) when (ex.ErrorCode == "NoSuchKey")
        {
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task<ListObjectsResult> ListAsync(
        string? prefix = null,
        int maxKeys = 1000,
        string? continuationToken = null,
        CancellationToken cancellationToken = default)
    {
        if (maxKeys <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxKeys), maxKeys, "maxKeys must be positive.");
        }

        var request = new ListObjectsRequest(_options.BucketName)
        {
            Prefix = prefix,
            MaxKeys = maxKeys,
            Marker = continuationToken,
        };

        var listing = await Task.Run(() => _oss.ListObjects(request), cancellationToken).ConfigureAwait(false);

        // OSS ListObjects, like S3 ListObjectsV2, does not report per-object content type —
        // ObjectMetadata.ContentType stays null; callers needing it follow up with GetMetadataAsync.
        var items = (listing.ObjectSummaries ?? Enumerable.Empty<OssObjectSummary>())
            .Select(o => new ObjectMetadata(
                o.Key,
                o.Size,
                ContentType: null,
                o.ETag,
                ToUtcOffset(o.LastModified)))
            .ToList();

        return new ListObjectsResult(items, listing.IsTruncated ? listing.NextMarker : null);
    }

    private static ObjectMetadata ToMetadata(string objectKey, OssObjectMetadata head)
        => new(
            objectKey,
            head.ContentLength,
            head.ContentType,
            head.ETag,
            ToUtcOffset(head.LastModified));

    // OSS reports timestamps as DateTime (parsed from the HTTP Last-Modified header, UTC wall
    // clock) but may leave Kind unspecified — pin it to UTC before widening to DateTimeOffset.
    private static DateTimeOffset ToUtcOffset(DateTime value)
        => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static void ValidateContentType(string contentType, UploadConstraints? constraints)
    {
        if (constraints?.AllowedContentTypes is { Count: > 0 } allow &&
            !allow.Contains(contentType, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Content type '{contentType}' is not permitted by the upload policy.");
        }
    }

    private static string GetLeafName(string objectKey)
    {
        var i = objectKey.LastIndexOf('/');
        return i < 0 ? objectKey : objectKey[(i + 1)..];
    }

    private static string SanitizeFileName(string name)
    {
        Span<char> buf = stackalloc char[name.Length];
        var len = 0;
        foreach (var ch in name)
        {
            // Strip spaces too: a space would land unencoded in the presigned URL's
            // response-content-disposition param (the Aliyun OSS SDK does not encode it),
            // yielding a malformed download URL.
            if (ch == '"' || ch == '\\' || ch == ' ' || ch == '\r' || ch == '\n' || ch < 0x20)
            {
                continue;
            }

            buf[len++] = ch;
        }

        return len == 0 ? "download" : new string(buf[..len]);
    }

    private static partial class Log
    {
        [LoggerMessage(EventId = 1, Level = LogLevel.Debug,
            Message = "Uploaded object {ObjectKey} to OSS bucket {Bucket}")]
        public static partial void Uploaded(ILogger logger, string objectKey, string bucket);

        [LoggerMessage(EventId = 2, Level = LogLevel.Debug,
            Message = "Deleted object {ObjectKey} from OSS bucket {Bucket}")]
        public static partial void Deleted(ILogger logger, string objectKey, string bucket);
    }
}
