using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Transfer;
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

    /// <summary>
    /// Initializes a new instance of the <see cref="S3FileStorage"/> class.
    /// </summary>
    /// <param name="s3">The S3 client (Amazon S3 or MinIO-compatible).</param>
    /// <param name="options">Storage options (bucket name, region, etc.).</param>
    /// <param name="logger">Logger for S3 operations.</param>
    public S3FileStorage(IAmazonS3 s3, IOptions<StorageOptions> options, ILogger<S3FileStorage> logger)
    {
        _s3 = s3;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task PutAsync(string path, Stream content, string contentType, CancellationToken ct = default)
    {
        using var transferUtil = new TransferUtility(_s3);
        var request = new TransferUtilityUploadRequest
        {
            BucketName = _options.BucketName,
            Key = path,
            InputStream = content,
            ContentType = contentType,
        };

        await transferUtil.UploadAsync(request, ct);
        Log.ObjectPut(_logger, _options.BucketName, path);
    }

    /// <inheritdoc />
    public async Task<Stream> GetAsync(string path, CancellationToken ct = default)
    {
        var request = new GetObjectRequest
        {
            BucketName = _options.BucketName,
            Key = path,
        };

        var response = await _s3.GetObjectAsync(request, ct);
        Log.ObjectGet(_logger, _options.BucketName, path);
        return response.ResponseStream;
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string path, CancellationToken ct = default)
    {
        var request = new DeleteObjectRequest
        {
            BucketName = _options.BucketName,
            Key = path,
        };

        await _s3.DeleteObjectAsync(request, ct);
        Log.ObjectDeleted(_logger, _options.BucketName, path);
    }

    /// <inheritdoc />
    public async Task<bool> ExistsAsync(string path, CancellationToken ct = default)
    {
        try
        {
            var request = new GetObjectMetadataRequest
            {
                BucketName = _options.BucketName,
                Key = path,
            };

            await _s3.GetObjectMetadataAsync(request, ct);
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public Task<Uri> GetPresignedUrlAsync(string path, TimeSpan expires, CancellationToken ct = default)
    {
        var request = new GetPreSignedUrlRequest
        {
            BucketName = _options.BucketName,
            Key = path,
            Expires = DateTime.UtcNow.Add(expires),
        };

        var url = _s3.GetPreSignedURL(request);
        Log.PresignedUrlGenerated(_logger, _options.BucketName, path);
        return Task.FromResult(new Uri(url));
    }

    private static partial class Log
    {
        [LoggerMessage(LogLevel.Debug, "Put object s3://{Bucket}/{Key}")]
        public static partial void ObjectPut(ILogger logger, string bucket, string key);

        [LoggerMessage(LogLevel.Debug, "Get object s3://{Bucket}/{Key}")]
        public static partial void ObjectGet(ILogger logger, string bucket, string key);

        [LoggerMessage(LogLevel.Debug, "Deleted object s3://{Bucket}/{Key}")]
        public static partial void ObjectDeleted(ILogger logger, string bucket, string key);

        [LoggerMessage(LogLevel.Debug, "Generated presigned URL for s3://{Bucket}/{Key}")]
        public static partial void PresignedUrlGenerated(ILogger logger, string bucket, string key);
    }
}
