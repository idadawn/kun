using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kun.Storage.S3;

/// <summary>
/// Amazon S3 (and S3-compatible: MinIO, Ceph, LocalStack) implementation of <see cref="IFileStorage"/>.
/// </summary>
/// <remarks>
/// <para>
/// This type's critical security guarantee: <see cref="GetPresignedDownloadAsync"/> ALWAYS
/// sets <c>Content-Disposition: attachment; filename="…"</c> on the signed URL when the
/// stored object's content type is one of the four active web content types enumerated in
/// <see cref="ActiveWebContentTypes.Value"/> — <c>text/html</c>, <c>image/svg+xml</c>,
/// <c>application/xhtml+xml</c>, <c>application/xml</c>. This holds regardless of whether
/// the caller sets <see cref="DownloadOptions.ForceDownload"/>.
/// </para>
/// </remarks>
public sealed partial class S3FileStorage : IFileStorage
{
    private readonly IAmazonS3 _s3;
    private readonly S3StorageOptions _options;
    private readonly ILogger<S3FileStorage> _logger;
    private readonly TimeProvider _timeProvider;

    /// <summary>Initializes a new instance of the <see cref="S3FileStorage"/> class.</summary>
    public S3FileStorage(
        IAmazonS3 s3,
        IOptions<S3StorageOptions> options,
        ILogger<S3FileStorage> logger,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(s3);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _s3 = s3;
        _options = options.Value;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;

        if (string.IsNullOrWhiteSpace(_options.BucketName))
        {
            throw new InvalidOperationException("S3StorageOptions.BucketName must be set before use.");
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

        var request = new PutObjectRequest
        {
            BucketName = _options.BucketName,
            Key = objectKey,
            InputStream = content,
            ContentType = contentType,
            AutoCloseStream = false,
            // Required when InputStream is non-seekable (e.g. a live upload
            // passing through an HttpRequest body). AWS SIGv4 otherwise needs
            // to hash the full payload before transmitting, which forces the
            // SDK to buffer everything in memory. Confidentiality is still
            // guaranteed by TLS; integrity relies on the HTTPS layer instead
            // of the signed-body hash.
            DisablePayloadSigning = true,
        };

        var response = await _s3.PutObjectAsync(request, cancellationToken).ConfigureAwait(false);
        Log.Uploaded(_logger, objectKey, _options.BucketName);

        var head = await _s3.GetObjectMetadataAsync(new GetObjectMetadataRequest
        {
            BucketName = _options.BucketName,
            Key = objectKey,
        }, cancellationToken).ConfigureAwait(false);

        if (constraints?.MaxBytes is { } max && head.ContentLength > max)
        {
            await _s3.DeleteObjectAsync(_options.BucketName, objectKey, cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException(
                $"Upload size {head.ContentLength} bytes exceeds MaxBytes={max}.");
        }

        return new ObjectMetadata(
            objectKey,
            head.ContentLength,
            head.Headers.ContentType ?? contentType,
            response.ETag,
            head.LastModified);
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

        // Peek at stored content type to decide whether to force attachment.
        var head = await _s3.GetObjectMetadataAsync(new GetObjectMetadataRequest
        {
            BucketName = _options.BucketName,
            Key = objectKey,
        }, cancellationToken).ConfigureAwait(false);

        var contentType = head.Headers.ContentType;
        var active = ActiveWebContentTypes.Contains(contentType);
        var forceAttachment = active || (options?.ForceDownload ?? false);

        var request = new GetPreSignedUrlRequest
        {
            BucketName = _options.BucketName,
            Key = objectKey,
            Expires = expiresAt.UtcDateTime,
            Verb = HttpVerb.GET,
        };

        var responseHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? disposition = null;

        if (forceAttachment)
        {
            var fileName = SanitizeFileName(options?.DownloadFileName ?? GetLeafName(objectKey));
            disposition = $"attachment; filename=\"{fileName}\"";
            request.ResponseHeaderOverrides.ContentDisposition = disposition;
        }

        // Apply caller-provided overrides first so our force-attachment clamp
        // below cannot be shadowed by a hostile caller dict in the returned
        // PresignedDownloadUrl.ResponseHeaders (the signed URL itself still
        // carries the forced disposition via ResponseHeaderOverrides, but
        // server-side proxies that echo the dict to their own responses would
        // otherwise lose the hardening).
        if (options?.ResponseHeaders is { Count: > 0 } custom)
        {
            foreach (var (k, v) in custom)
            {
                responseHeaders[k] = v;
            }
        }

        if (forceAttachment)
        {
            responseHeaders["Content-Disposition"] = disposition!;
        }

        var url = await _s3.GetPreSignedURLAsync(request).ConfigureAwait(false);

        return new PresignedDownloadUrl(
            new Uri(url, UriKind.Absolute),
            responseHeaders,
            expiresAt,
            active);
    }

    /// <inheritdoc/>
    public async Task<PresignedUploadUrl> GetPresignedUploadAsync(
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

        var request = new GetPreSignedUrlRequest
        {
            BucketName = _options.BucketName,
            Key = objectKey,
            Expires = expiresAt.UtcDateTime,
            Verb = HttpVerb.PUT,
            ContentType = contentType,
        };

        var url = await _s3.GetPreSignedURLAsync(request).ConfigureAwait(false);

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Content-Type"] = contentType,
        };

        return new PresignedUploadUrl(new Uri(url, UriKind.Absolute), headers, expiresAt);
    }

    /// <inheritdoc/>
    public async Task<bool> ExistsAsync(string objectKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);
        try
        {
            _ = await _s3.GetObjectMetadataAsync(new GetObjectMetadataRequest
            {
                BucketName = _options.BucketName,
                Key = objectKey,
            }, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
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

        await _s3.DeleteObjectAsync(_options.BucketName, objectKey, cancellationToken).ConfigureAwait(false);
        Log.Deleted(_logger, objectKey, _options.BucketName);
        return true;
    }

    /// <inheritdoc/>
    public async Task<ObjectMetadata?> GetMetadataAsync(string objectKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);
        try
        {
            var head = await _s3.GetObjectMetadataAsync(new GetObjectMetadataRequest
            {
                BucketName = _options.BucketName,
                Key = objectKey,
            }, cancellationToken).ConfigureAwait(false);

            return new ObjectMetadata(
                objectKey,
                head.ContentLength,
                head.Headers.ContentType ?? "application/octet-stream",
                head.ETag,
                head.LastModified);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
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

        var request = new ListObjectsV2Request
        {
            BucketName = _options.BucketName,
            Prefix = prefix,
            MaxKeys = maxKeys,
            ContinuationToken = continuationToken,
        };

        var response = await _s3.ListObjectsV2Async(request, cancellationToken).ConfigureAwait(false);

        // S3's ListObjectsV2 response does not include per-object content type —
        // fetching it would require a HEAD per item, which would defeat the point
        // of a paginated list. ObjectMetadata.ContentType is nullable precisely to
        // model this; callers needing the type call GetMetadataAsync afterwards.
        var items = (response.S3Objects ?? Enumerable.Empty<S3Object>())
            .Select(o => new ObjectMetadata(
                o.Key,
                o.Size,
                ContentType: null,
                o.ETag,
                o.LastModified))
            .ToList();

        return new ListObjectsResult(items, response.IsTruncated == true ? response.NextContinuationToken : null);
    }

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
            if (ch == '"' || ch == '\\' || ch == '\r' || ch == '\n' || ch < 0x20)
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
            Message = "Uploaded object {ObjectKey} to bucket {Bucket}")]
        public static partial void Uploaded(ILogger logger, string objectKey, string bucket);

        [LoggerMessage(EventId = 2, Level = LogLevel.Debug,
            Message = "Deleted object {ObjectKey} from bucket {Bucket}")]
        public static partial void Deleted(ILogger logger, string objectKey, string bucket);
    }
}
