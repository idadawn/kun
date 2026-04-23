using System.Net;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Amazon.S3;
using Amazon.S3.Model;
using Kun.Storage.Abstractions;
using Kun.Storage.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kun.Storage.Implementations;

/// <summary>
/// Amazon S3 / MinIO-compatible implementation of <see cref="IFileStorage"/>.
/// Uses the low-level <see cref="IAmazonS3"/> client directly — the
/// AWS-SDK <c>TransferUtility</c> is intentionally avoided because it
/// adds reflection-heavy code paths that block the AOT analyzer.
/// </summary>
/// <remarks>
/// Active-web content types are persisted with
/// <c>Content-Disposition: attachment</c>; the same header is re-applied
/// to presigned download URLs as a defence-in-depth override so clients
/// cannot inline-render a stored HTML page via a public URL.
/// </remarks>
public sealed partial class S3FileStorage : IFileStorage
{
    private readonly IAmazonS3 _s3;
    private readonly string _bucket;
    private readonly ILogger<S3FileStorage> _logger;

    /// <summary>Creates a new <see cref="S3FileStorage"/>.</summary>
    public S3FileStorage(IAmazonS3 s3, IOptions<S3StorageOptions> options, ILogger<S3FileStorage> logger)
    {
        ArgumentNullException.ThrowIfNull(s3);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        var value = options.Value;
        ArgumentException.ThrowIfNullOrWhiteSpace(value.BucketName);
        _s3 = s3;
        _bucket = value.BucketName;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ObjectMetadata> PutObjectAsync(
        string objectKey,
        Stream content,
        string contentType,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);

        // Hash the body before upload when the stream is seekable so callers
        // get a Sha256 back. Non-seekable streams skip the hash to avoid
        // buffering huge uploads in memory.
        string? sha256 = null;
        long size = content.CanSeek ? content.Length : -1;
        if (content.CanSeek)
        {
            var position = content.Position;
            using var hash = SHA256.Create();
            var digest = await hash.ComputeHashAsync(content, ct).ConfigureAwait(false);
            sha256 = Convert.ToHexStringLower(digest);
            content.Position = position;
        }

        var request = new PutObjectRequest
        {
            BucketName = _bucket,
            Key = objectKey,
            InputStream = content,
            ContentType = contentType,
            AutoCloseStream = false,
        };

        var disposition = ContentDispositionPolicy.BuildHeaderValue(objectKey, contentType);
        if (disposition is not null)
        {
            request.Headers.ContentDisposition = disposition;
        }

        _ = await _s3.PutObjectAsync(request, ct).ConfigureAwait(false);
        Log.ObjectPut(_logger, _bucket, objectKey);

        return new ObjectMetadata(
            ObjectKey: objectKey,
            Size: size < 0 ? 0 : size,
            ContentType: contentType,
            Sha256: sha256,
            LastModified: DateTimeOffset.UtcNow);
    }

    /// <inheritdoc />
    public async Task<Stream> GetObjectAsync(string objectKey, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);
        try
        {
            var response = await _s3.GetObjectAsync(
                new GetObjectRequest { BucketName = _bucket, Key = objectKey },
                ct).ConfigureAwait(false);
            Log.ObjectGet(_logger, _bucket, objectKey);
            return response.ResponseStream;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            throw new FileNotFoundException($"Object '{objectKey}' not found in bucket '{_bucket}'.", objectKey, ex);
        }
    }

    /// <inheritdoc />
    public async Task<ObjectMetadata?> HeadObjectAsync(string objectKey, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);
        try
        {
            var response = await _s3.GetObjectMetadataAsync(
                new GetObjectMetadataRequest { BucketName = _bucket, Key = objectKey },
                ct).ConfigureAwait(false);
            return new ObjectMetadata(
                ObjectKey: objectKey,
                Size: response.ContentLength,
                ContentType: response.Headers.ContentType,
                Sha256: null,
                LastModified: response.LastModified is { Ticks: > 0 } lm ? lm : DateTimeOffset.UtcNow);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public async Task DeleteObjectAsync(string objectKey, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);
        await _s3.DeleteObjectAsync(
            new DeleteObjectRequest { BucketName = _bucket, Key = objectKey },
            ct).ConfigureAwait(false);
        Log.ObjectDeleted(_logger, _bucket, objectKey);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<ObjectMetadata> ListObjectsAsync(
        string? prefix = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        string? continuationToken = null;
        do
        {
            var response = await _s3.ListObjectsV2Async(
                new ListObjectsV2Request
                {
                    BucketName = _bucket,
                    Prefix = prefix,
                    ContinuationToken = continuationToken,
                },
                ct).ConfigureAwait(false);

            foreach (var item in response.S3Objects ?? [])
            {
                yield return new ObjectMetadata(
                    ObjectKey: item.Key,
                    Size: item.Size,
                    ContentType: null,
                    Sha256: null,
                    LastModified: item.LastModified is { Ticks: > 0 } lm ? lm : DateTimeOffset.UtcNow);
            }

            continuationToken = response.IsTruncated == true ? response.NextContinuationToken : null;
        }
        while (continuationToken is not null);
    }

    /// <inheritdoc />
    public Task<PresignedUploadUrl> CreatePresignedUploadUrlAsync(
        string objectKey,
        TimeSpan expiresIn,
        string? contentType = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);
        if (expiresIn <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(expiresIn), "Expiry must be positive.");
        }

        var expiry = DateTime.UtcNow.Add(expiresIn);
        var request = new GetPreSignedUrlRequest
        {
            BucketName = _bucket,
            Key = objectKey,
            Verb = HttpVerb.PUT,
            Expires = expiry,
            ContentType = contentType,
        };

        var url = _s3.GetPreSignedURL(request);
        Log.PresignedUrlGenerated(_logger, _bucket, objectKey, "PUT");
        return Task.FromResult(new PresignedUploadUrl(
            Url: new Uri(url),
            Expiry: expiry,
            ObjectKey: objectKey,
            RequiredContentType: contentType));
    }

    /// <inheritdoc />
    public Task<PresignedDownloadUrl> CreatePresignedDownloadUrlAsync(
        string objectKey,
        TimeSpan expiresIn,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);
        if (expiresIn <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(expiresIn), "Expiry must be positive.");
        }

        var expiry = DateTime.UtcNow.Add(expiresIn);
        var url = _s3.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = _bucket,
            Key = objectKey,
            Verb = HttpVerb.GET,
            Expires = expiry,
        });

        Log.PresignedUrlGenerated(_logger, _bucket, objectKey, "GET");
        return Task.FromResult(new PresignedDownloadUrl(
            Url: new Uri(url),
            Expiry: expiry,
            ObjectKey: objectKey));
    }

    private static partial class Log
    {
        [LoggerMessage(LogLevel.Debug, "Put object s3://{Bucket}/{Key}")]
        public static partial void ObjectPut(ILogger logger, string bucket, string key);

        [LoggerMessage(LogLevel.Debug, "Get object s3://{Bucket}/{Key}")]
        public static partial void ObjectGet(ILogger logger, string bucket, string key);

        [LoggerMessage(LogLevel.Debug, "Deleted object s3://{Bucket}/{Key}")]
        public static partial void ObjectDeleted(ILogger logger, string bucket, string key);

        [LoggerMessage(LogLevel.Debug, "Generated {Verb} presigned URL for s3://{Bucket}/{Key}")]
        public static partial void PresignedUrlGenerated(ILogger logger, string bucket, string key, string verb);
    }
}
