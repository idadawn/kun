using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
            // Only disable payload signing for non-seekable streams, where SIGv4
            // would otherwise buffer the full payload in memory to hash it up front
            // (e.g. a live upload passing through an HttpRequest body). Seekable
            // streams (MemoryStream, FileStream, buffered request bodies) can be
            // hashed in place, so signing works over both HTTP and HTTPS. AWS rejects
            // DisablePayloadSigning=true over plain HTTP, so callers sending non-
            // seekable streams must still use an HTTPS endpoint — integrity is then
            // guaranteed by TLS instead of the signed body hash.
            DisablePayloadSigning = !content.CanSeek,
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
    public Task<PresignedPostUpload> GetPresignedPostUploadAsync(
        string objectKey,
        UploadConstraints? constraints = null,
        TimeSpan? expiresIn = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);

        if (string.IsNullOrWhiteSpace(_options.AccessKey) || string.IsNullOrWhiteSpace(_options.SecretKey))
        {
            throw new InvalidOperationException(
                "GetPresignedPostUploadAsync requires S3StorageOptions.AccessKey and " +
                "S3StorageOptions.SecretKey to be set so the POST policy can be signed. " +
                "Configure static credentials on S3StorageOptions; the AWSSDK default credential " +
                "resolution chain is not consulted for POST-policy signing.");
        }

        if (string.IsNullOrWhiteSpace(_options.Region))
        {
            throw new InvalidOperationException(
                "GetPresignedPostUploadAsync requires S3StorageOptions.Region to be set so the " +
                "signing credential scope can be constructed.");
        }

        // Extract the single allowed content type (if any). A multi-entry whitelist cannot be
        // represented as a single exact-match policy condition without introducing a separate
        // form variant per content type — reject that case with a remediation message.
        string? fixedContentType = null;
        if (constraints?.AllowedContentTypes is { Count: > 0 } allow)
        {
            if (allow.Count > 1)
            {
                throw new NotSupportedException(
                    "Presigned POST policies with more than one allowed content type are not " +
                    "supported: the signed policy encodes Content-Type as an equality condition, " +
                    "so multiple allowed types would require multiple signed policies. " +
                    "Either narrow UploadConstraints.AllowedContentTypes to a single entry, or " +
                    "omit it and validate the content type server-side after upload.");
            }

            fixedContentType = allow.First();
        }

        var lifetime = expiresIn ?? _options.DefaultPresignedUrlLifetime;
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var expiresAt = new DateTimeOffset(now, TimeSpan.Zero).Add(lifetime);

        var amzDate = now.ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture);
        var shortDate = now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var credentialScope = string.Create(
            CultureInfo.InvariantCulture,
            $"{_options.AccessKey}/{shortDate}/{_options.Region}/s3/aws4_request");

        // Build policy JSON. Order matters for readability only — S3 parses conditions as a
        // set, not an ordered list. We keep the order {bucket, key, acl, content-length-range,
        // Content-Type, x-amz-*} so tests can compare a stable document shape.
        var policyJson = BuildPostPolicyJson(
            bucket: _options.BucketName,
            objectKey: objectKey,
            acl: "private",
            maxBytes: constraints?.MaxBytes,
            contentType: fixedContentType,
            credentialScope: credentialScope,
            amzDate: amzDate,
            expiresAt: expiresAt.UtcDateTime);

        var policyBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(policyJson));

        // SigV4: derive signing key and HMAC the base64 policy.
        var signingKey = DeriveSigningKey(_options.SecretKey!, shortDate, _options.Region!, "s3");
        var signatureBytes = HMACSHA256.HashData(signingKey, Encoding.UTF8.GetBytes(policyBase64));
        var signature = Convert.ToHexStringLower(signatureBytes);

        // Assemble form fields in the order the S3 docs recommend; callers must iterate and
        // append each field in order to their multipart body BEFORE the `file` field.
        var form = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["key"] = objectKey,
            ["acl"] = "private",
        };

        if (fixedContentType is not null)
        {
            form["Content-Type"] = fixedContentType;
        }

        form["x-amz-algorithm"] = "AWS4-HMAC-SHA256";
        form["x-amz-credential"] = credentialScope;
        form["x-amz-date"] = amzDate;
        form["policy"] = policyBase64;
        form["x-amz-signature"] = signature;

        var postUrl = BuildBucketEndpointUri();
        Log.PostPolicySigned(_logger, objectKey, _options.BucketName);
        return Task.FromResult(new PresignedPostUpload(postUrl, form, expiresAt));
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
    public async Task<Stream> OpenReadAsync(string objectKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);
        try
        {
            var resp = await _s3.GetObjectAsync(new GetObjectRequest
            {
                BucketName = _options.BucketName,
                Key = objectKey,
            }, cancellationToken).ConfigureAwait(false);
            return resp.ResponseStream;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
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

    /// <summary>
    /// Builds the AWS S3 POST policy document as compact JSON, matching the format described
    /// at <see href="https://docs.aws.amazon.com/AmazonS3/latest/userguide/HTTPPOSTForms.html"/>.
    /// The conditions array includes bucket, key (exact-match), acl, optional
    /// content-length-range, optional Content-Type equality, and the SigV4 credential /
    /// algorithm / date fields that must match the accompanying form fields.
    /// </summary>
    private static string BuildPostPolicyJson(
        string bucket,
        string objectKey,
        string acl,
        long? maxBytes,
        string? contentType,
        string credentialScope,
        string amzDate,
        DateTime expiresAt)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("expiration", expiresAt.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture));
            writer.WritePropertyName("conditions");
            writer.WriteStartArray();

            // {"bucket": "<bucket>"}
            writer.WriteStartObject();
            writer.WriteString("bucket", bucket);
            writer.WriteEndObject();

            // {"key": "<objectKey>"}  (exact match)
            writer.WriteStartObject();
            writer.WriteString("key", objectKey);
            writer.WriteEndObject();

            // {"acl": "<acl>"}
            writer.WriteStartObject();
            writer.WriteString("acl", acl);
            writer.WriteEndObject();

            // ["content-length-range", 0, maxBytes]
            if (maxBytes is { } max)
            {
                writer.WriteStartArray();
                writer.WriteStringValue("content-length-range");
                writer.WriteNumberValue(0);
                writer.WriteNumberValue(max);
                writer.WriteEndArray();
            }

            // {"Content-Type": "<fixed>"}  (only when a single allowed type is supplied)
            if (contentType is not null)
            {
                writer.WriteStartObject();
                writer.WriteString("Content-Type", contentType);
                writer.WriteEndObject();
            }

            // {"x-amz-algorithm": "AWS4-HMAC-SHA256"}
            writer.WriteStartObject();
            writer.WriteString("x-amz-algorithm", "AWS4-HMAC-SHA256");
            writer.WriteEndObject();

            // {"x-amz-credential": "<scope>"}
            writer.WriteStartObject();
            writer.WriteString("x-amz-credential", credentialScope);
            writer.WriteEndObject();

            // {"x-amz-date": "<amzDate>"}
            writer.WriteStartObject();
            writer.WriteString("x-amz-date", amzDate);
            writer.WriteEndObject();

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>
    /// Derives an AWS SigV4 signing key by chaining HMAC-SHA256 over date, region, service,
    /// and the "aws4_request" terminator. See the AWS SigV4 spec §3.3.
    /// </summary>
    private static byte[] DeriveSigningKey(string secretKey, string shortDate, string region, string service)
    {
        var kSecret = Encoding.UTF8.GetBytes("AWS4" + secretKey);
        var kDate = HMACSHA256.HashData(kSecret, Encoding.UTF8.GetBytes(shortDate));
        var kRegion = HMACSHA256.HashData(kDate, Encoding.UTF8.GetBytes(region));
        var kService = HMACSHA256.HashData(kRegion, Encoding.UTF8.GetBytes(service));
        return HMACSHA256.HashData(kService, Encoding.UTF8.GetBytes("aws4_request"));
    }

    /// <summary>
    /// Produces the bucket endpoint URL clients POST uploads to. Prefers
    /// <see cref="S3StorageOptions.ServiceUrl"/> (MinIO / LocalStack: path-style
    /// <c>{serviceUrl}/{bucket}</c>); falls back to the regional virtual-host style
    /// <c>https://{bucket}.s3.{region}.amazonaws.com</c> otherwise.
    /// </summary>
    private Uri BuildBucketEndpointUri()
    {
        if (_options.ServiceUrl is not null)
        {
            var baseUrl = _options.ServiceUrl.AbsoluteUri;
            if (_options.ForcePathStyle)
            {
                if (!baseUrl.EndsWith('/'))
                {
                    baseUrl += "/";
                }

                return new Uri(baseUrl + Uri.EscapeDataString(_options.BucketName), UriKind.Absolute);
            }

            // Virtual-host style against a custom endpoint is rare, but honor it.
            var hostInsert = new UriBuilder(_options.ServiceUrl)
            {
                Host = _options.BucketName + "." + _options.ServiceUrl.Host,
            };
            return hostInsert.Uri;
        }

        var region = _options.Region ?? "us-east-1";
        return new Uri(
            string.Create(CultureInfo.InvariantCulture, $"https://{_options.BucketName}.s3.{region}.amazonaws.com"),
            UriKind.Absolute);
    }

    private static partial class Log
    {
        [LoggerMessage(EventId = 1, Level = LogLevel.Debug,
            Message = "Uploaded object {ObjectKey} to bucket {Bucket}")]
        public static partial void Uploaded(ILogger logger, string objectKey, string bucket);

        [LoggerMessage(EventId = 2, Level = LogLevel.Debug,
            Message = "Deleted object {ObjectKey} from bucket {Bucket}")]
        public static partial void Deleted(ILogger logger, string objectKey, string bucket);

        [LoggerMessage(EventId = 3, Level = LogLevel.Debug,
            Message = "Signed POST policy for object {ObjectKey} in bucket {Bucket}")]
        public static partial void PostPolicySigned(ILogger logger, string objectKey, string bucket);
    }
}
