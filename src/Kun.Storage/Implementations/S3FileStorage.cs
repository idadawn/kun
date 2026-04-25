using System.Net;
using System.Runtime.CompilerServices;
using Amazon.S3;
using Amazon.S3.Model;
using Kun.Storage.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kun.Storage.Implementations;

/// <summary>
/// Amazon S3 / MinIO-compatible implementation of <see cref="IFileStorage"/>.
/// </summary>
public sealed partial class S3FileStorage : IFileStorage
{
    private readonly IAmazonS3 _s3;
    private readonly StorageOptions _options;
    private readonly ILogger<S3FileStorage> _logger;

    /// <summary>Initializes a new instance.</summary>
    public S3FileStorage(IAmazonS3 s3, IOptions<StorageOptions> options, ILogger<S3FileStorage> logger)
    {
        ArgumentNullException.ThrowIfNull(s3);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _s3 = s3;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task PutObjectAsync(
        string path,
        Stream content,
        PutObjectOptions options,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(options);

        var request = new PutObjectRequest
        {
            BucketName = _options.BucketName,
            Key = path,
            InputStream = content,
            ContentType = options.ContentType,
            AutoCloseStream = false,
        };

        var enforcedDisposition = ContentDispositionPolicy.EnforceAttachment(
            options.ContentType, options.ContentDisposition, options.FileName);
        if (!string.IsNullOrEmpty(enforcedDisposition))
        {
            request.Headers.ContentDisposition = enforcedDisposition;
        }

        if (!string.IsNullOrEmpty(options.CacheControl))
        {
            request.Headers.CacheControl = options.CacheControl;
        }

        if (options.UserMetadata is { Count: > 0 })
        {
            foreach (var kv in options.UserMetadata)
            {
                request.Metadata[kv.Key] = kv.Value;
            }
        }

        await _s3.PutObjectAsync(request, ct).ConfigureAwait(false);
        Log.ObjectPut(_logger, _options.BucketName, path);
    }

    /// <inheritdoc />
    public async Task<ObjectDownload> GetObjectAsync(string path, CancellationToken ct = default)
    {
        var response = await _s3.GetObjectAsync(new GetObjectRequest
        {
            BucketName = _options.BucketName,
            Key = path,
        }, ct).ConfigureAwait(false);

        var metadata = BuildMetadata(
            response.Headers.ContentType,
            response.Headers.ContentLength,
            response.ETag,
            response.LastModified,
            response.Headers.ContentDisposition,
            response.Metadata);

        Log.ObjectGet(_logger, _options.BucketName, path);
        return new ObjectDownload(response.ResponseStream, metadata);
    }

    /// <inheritdoc />
    public async Task<ObjectMetadata?> HeadObjectAsync(string path, CancellationToken ct = default)
    {
        try
        {
            var response = await _s3.GetObjectMetadataAsync(new GetObjectMetadataRequest
            {
                BucketName = _options.BucketName,
                Key = path,
            }, ct).ConfigureAwait(false);

            return BuildMetadata(
                response.Headers.ContentType,
                response.Headers.ContentLength,
                response.ETag,
                response.LastModified,
                response.Headers.ContentDisposition,
                response.Metadata);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public async Task DeleteObjectAsync(string path, CancellationToken ct = default)
    {
        await _s3.DeleteObjectAsync(new DeleteObjectRequest
        {
            BucketName = _options.BucketName,
            Key = path,
        }, ct).ConfigureAwait(false);
        Log.ObjectDeleted(_logger, _options.BucketName, path);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<ObjectSummary> ListObjectsAsync(
        string prefix,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        string? continuationToken = null;

        do
        {
            var response = await _s3.ListObjectsV2Async(new ListObjectsV2Request
            {
                BucketName = _options.BucketName,
                Prefix = prefix,
                ContinuationToken = continuationToken,
            }, ct).ConfigureAwait(false);

            if (response.S3Objects is { Count: > 0 })
            {
                foreach (var item in response.S3Objects)
                {
                    ct.ThrowIfCancellationRequested();
                    yield return new ObjectSummary(
                        item.Key,
                        item.Size,
                        ToUtc(item.LastModified),
                        item.ETag);
                }
            }

            continuationToken = response.IsTruncated == true ? response.NextContinuationToken : null;
        }
        while (continuationToken is not null);
    }

    /// <inheritdoc />
    public Task<PresignedUploadUrl> CreatePresignedUploadUrlAsync(
        string path,
        PresignedUploadRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var expiresAt = DateTimeOffset.UtcNow.Add(request.ExpiresIn);

        var req = new GetPreSignedUrlRequest
        {
            BucketName = _options.BucketName,
            Key = path,
            Verb = HttpVerb.PUT,
            Expires = expiresAt.UtcDateTime,
            Protocol = ResolveProtocol(),
        };
        if (!string.IsNullOrEmpty(request.ContentType))
        {
            req.ContentType = request.ContentType;
        }

        var url = _s3.GetPreSignedURL(req);
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(request.ContentType))
        {
            headers["Content-Type"] = request.ContentType;
        }

        Log.PresignedUploadUrlGenerated(_logger, _options.BucketName, path);
        return Task.FromResult(new PresignedUploadUrl(new Uri(url), "PUT", headers, expiresAt));
    }

    /// <inheritdoc />
    public async Task<PresignedDownloadUrl> CreatePresignedDownloadUrlAsync(
        string path,
        PresignedDownloadRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var expiresAt = DateTimeOffset.UtcNow.Add(request.ExpiresIn);

        var dispositionOverride = request.ResponseContentDisposition;
        if (dispositionOverride is null)
        {
            // Probe the object's stored content-type so we can enforce attachment
            // disposition on active-web MIME types (HTML/SVG/XML/JS/WASM).
            var metadata = await HeadObjectAsync(path, ct).ConfigureAwait(false);
            if (metadata is not null
                && ContentDispositionPolicy.IsUnsafeForInlineDisplay(metadata.ContentType))
            {
                dispositionOverride = ContentDispositionPolicy.EnforceAttachment(
                    metadata.ContentType, currentDisposition: null, fileName: Path.GetFileName(path));
            }
        }

        var req = new GetPreSignedUrlRequest
        {
            BucketName = _options.BucketName,
            Key = path,
            Verb = HttpVerb.GET,
            Expires = expiresAt.UtcDateTime,
            Protocol = ResolveProtocol(),
        };
        if (!string.IsNullOrEmpty(dispositionOverride))
        {
            req.ResponseHeaderOverrides.ContentDisposition = dispositionOverride;
        }

        var url = _s3.GetPreSignedURL(req);
        Log.PresignedDownloadUrlGenerated(_logger, _options.BucketName, path);
        return new PresignedDownloadUrl(new Uri(url), expiresAt);
    }

    private static ObjectMetadata BuildMetadata(
        string? contentType,
        long contentLength,
        string? eTag,
        DateTime? lastModified,
        string? contentDisposition,
        MetadataCollection metadata)
    {
        Dictionary<string, string>? userMeta = null;
        if (metadata.Count > 0)
        {
            userMeta = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var key in metadata.Keys)
            {
                // AWS SDK returns keys with the "x-amz-meta-" prefix — strip it.
                var normalizedKey = key.StartsWith("x-amz-meta-", StringComparison.OrdinalIgnoreCase)
                    ? key["x-amz-meta-".Length..]
                    : key;
                userMeta[normalizedKey] = metadata[key];
            }
        }

        return new ObjectMetadata(
            contentType ?? "application/octet-stream",
            contentLength,
            eTag,
            ToUtc(lastModified),
            contentDisposition,
            userMeta);
    }

    private static DateTimeOffset ToUtc(DateTime? value) =>
        value.HasValue
            ? new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc))
            : DateTimeOffset.UtcNow;

    /// <summary>
    /// Resolves the presigned URL scheme from the underlying S3 client's ServiceURL.
    /// AWS SDK defaults <see cref="GetPreSignedUrlRequest.Protocol"/> to HTTPS regardless of
    /// <c>AmazonS3Config.UseHttp</c>, so MinIO / LocalStack setups with an <c>http://</c>
    /// ServiceURL would otherwise emit <c>https://</c> URLs that fail a real HttpClient
    /// request with "Cannot determine the frame size".
    /// </summary>
    private Protocol ResolveProtocol() =>
        _s3.Config.ServiceURL is { } su
        && su.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            ? Protocol.HTTP
            : Protocol.HTTPS;

    private static partial class Log
    {
        [LoggerMessage(LogLevel.Debug, "Put object s3://{Bucket}/{Key}")]
        public static partial void ObjectPut(ILogger logger, string bucket, string key);

        [LoggerMessage(LogLevel.Debug, "Get object s3://{Bucket}/{Key}")]
        public static partial void ObjectGet(ILogger logger, string bucket, string key);

        [LoggerMessage(LogLevel.Debug, "Deleted object s3://{Bucket}/{Key}")]
        public static partial void ObjectDeleted(ILogger logger, string bucket, string key);

        [LoggerMessage(LogLevel.Debug, "Generated presigned PUT URL for s3://{Bucket}/{Key}")]
        public static partial void PresignedUploadUrlGenerated(ILogger logger, string bucket, string key);

        [LoggerMessage(LogLevel.Debug, "Generated presigned GET URL for s3://{Bucket}/{Key}")]
        public static partial void PresignedDownloadUrlGenerated(ILogger logger, string bucket, string key);
    }
}
