using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kun.Storage.Local;

/// <summary>
/// <see cref="IFileStorage"/> implementation that stores objects on the local filesystem.
/// Intended for development, tests, and single-node deployments. Presigned URLs are
/// developer-visible query-string-token URLs — validation is host responsibility.
/// </summary>
public sealed partial class LocalFileSystemStorage : IFileStorage
{
    private readonly LocalFileStorageOptions _options;
    private readonly ILogger<LocalFileSystemStorage> _logger;
    private readonly TimeProvider _timeProvider;

    /// <summary>Initializes a new instance of the <see cref="LocalFileSystemStorage"/> class.</summary>
    /// <param name="options">Storage options (root path, public base URL).</param>
    /// <param name="logger">Logger.</param>
    /// <param name="timeProvider">Clock used for presigned URL expiry and metadata timestamps.</param>
    public LocalFileSystemStorage(
        IOptions<LocalFileStorageOptions> options,
        ILogger<LocalFileSystemStorage> logger,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options.Value;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;

        if (string.IsNullOrWhiteSpace(_options.RootPath))
        {
            throw new InvalidOperationException(
                "LocalFileStorageOptions.RootPath must be set to an absolute path before use.");
        }

        Directory.CreateDirectory(_options.RootPath);
    }

    /// <inheritdoc/>
    public async Task<ObjectMetadata> UploadAsync(
        string objectKey,
        Stream content,
        string contentType,
        UploadConstraints? constraints = null,
        CancellationToken cancellationToken = default)
    {
        ValidateKey(objectKey);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);

        ValidateContentType(contentType, constraints);

        var target = ResolvePath(objectKey);
        var targetDir = Path.GetDirectoryName(target);
        if (!string.IsNullOrEmpty(targetDir))
        {
            Directory.CreateDirectory(targetDir);
        }

        long size;
        await using (var fs = new FileStream(
            target, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 81920, useAsync: true))
        {
            await content.CopyToAsync(fs, cancellationToken).ConfigureAwait(false);
            size = fs.Length;
        }

        if (constraints?.MaxBytes is { } max && size > max)
        {
            // Revert: oversized uploads must not leave a partial object behind.
            try { File.Delete(target); } catch (IOException) { /* best-effort cleanup */ }
            throw new InvalidOperationException(
                $"Upload size {size} bytes exceeds MaxBytes={max}.");
        }

        WriteContentType(target, contentType);

        var lastModified = new DateTimeOffset(File.GetLastWriteTimeUtc(target), TimeSpan.Zero);
        var etag = ComputeETag(size, lastModified);

        Log.Uploaded(_logger, objectKey, size, contentType);
        return new ObjectMetadata(objectKey, size, contentType, etag, lastModified);
    }

    /// <inheritdoc/>
    public Task<PresignedDownloadUrl> GetPresignedDownloadAsync(
        string objectKey,
        DownloadOptions? options = null,
        TimeSpan? expiresIn = null,
        CancellationToken cancellationToken = default)
    {
        ValidateKey(objectKey);

        var lifetime = expiresIn ?? _options.DefaultPresignedUrlLifetime;
        var expiresAt = _timeProvider.GetUtcNow().Add(lifetime);

        var contentType = ReadContentType(ResolvePath(objectKey));
        var active = ActiveWebContentTypes.Contains(contentType);
        var forceAttachment = active || (options?.ForceDownload ?? false);

        var query = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["expires"] = expiresAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
        };

        var responseHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (forceAttachment)
        {
            var fileName = SanitizeFileName(options?.DownloadFileName ?? Path.GetFileName(objectKey));
            var disposition = $"attachment; filename=\"{fileName}\"";
            query["disposition"] = disposition;
            responseHeaders["Content-Disposition"] = disposition;
        }

        if (options?.ResponseHeaders is { Count: > 0 } custom)
        {
            foreach (var (k, v) in custom)
            {
                responseHeaders[k] = v;
            }
        }

        var uri = BuildUri(_options.PublicBaseUrl, objectKey, query);
        return Task.FromResult(new PresignedDownloadUrl(uri, responseHeaders, expiresAt, active));
    }

    /// <inheritdoc/>
    public Task<PresignedUploadUrl> GetPresignedUploadAsync(
        string objectKey,
        string contentType,
        UploadConstraints? constraints = null,
        TimeSpan? expiresIn = null,
        CancellationToken cancellationToken = default)
    {
        ValidateKey(objectKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
        ValidateContentType(contentType, constraints);

        var lifetime = expiresIn ?? _options.DefaultPresignedUrlLifetime;
        var expiresAt = _timeProvider.GetUtcNow().Add(lifetime);

        var query = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["expires"] = expiresAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
            ["contentType"] = contentType,
        };

        var uri = BuildUri(_options.PublicBaseUrl, objectKey, query);

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Content-Type"] = contentType,
        };

        if (constraints?.MaxBytes is { } max)
        {
            headers["X-Kun-Max-Bytes"] = max.ToString(CultureInfo.InvariantCulture);
        }

        return Task.FromResult(new PresignedUploadUrl(uri, headers, expiresAt));
    }

    /// <inheritdoc/>
    public Task<bool> ExistsAsync(string objectKey, CancellationToken cancellationToken = default)
    {
        ValidateKey(objectKey);
        return Task.FromResult(File.Exists(ResolvePath(objectKey)));
    }

    /// <inheritdoc/>
    public Task<bool> DeleteAsync(string objectKey, CancellationToken cancellationToken = default)
    {
        ValidateKey(objectKey);
        var path = ResolvePath(objectKey);
        if (!File.Exists(path))
        {
            return Task.FromResult(false);
        }

        File.Delete(path);
        var sidecar = path + ".contenttype";
        if (File.Exists(sidecar))
        {
            File.Delete(sidecar);
        }

        Log.Deleted(_logger, objectKey);
        return Task.FromResult(true);
    }

    /// <inheritdoc/>
    public Task<ObjectMetadata?> GetMetadataAsync(string objectKey, CancellationToken cancellationToken = default)
    {
        ValidateKey(objectKey);
        var path = ResolvePath(objectKey);
        if (!File.Exists(path))
        {
            return Task.FromResult<ObjectMetadata?>(null);
        }

        var info = new FileInfo(path);
        var lastModified = new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero);
        var contentType = ReadContentType(path) ?? "application/octet-stream";
        var etag = ComputeETag(info.Length, lastModified);
        return Task.FromResult<ObjectMetadata?>(
            new ObjectMetadata(objectKey, info.Length, contentType, etag, lastModified));
    }

    /// <inheritdoc/>
    public Task<ListObjectsResult> ListAsync(
        string? prefix = null,
        int maxKeys = 1000,
        string? continuationToken = null,
        CancellationToken cancellationToken = default)
    {
        if (maxKeys <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxKeys), maxKeys, "maxKeys must be positive.");
        }

        var normalizedPrefix = prefix ?? string.Empty;
        var root = _options.RootPath;

        var all = Directory
            .EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(p => !p.EndsWith(".contenttype", StringComparison.Ordinal))
            .Select(p => ToObjectKey(root, p))
            .Where(k => k.StartsWith(normalizedPrefix, StringComparison.Ordinal))
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();

        var skip = 0;
        if (!string.IsNullOrEmpty(continuationToken) &&
            int.TryParse(continuationToken, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            skip = parsed;
        }

        var page = all.Skip(skip).Take(maxKeys).ToList();
        string? next = (skip + page.Count < all.Count)
            ? (skip + page.Count).ToString(CultureInfo.InvariantCulture)
            : null;

        var results = new List<ObjectMetadata>(page.Count);
        foreach (var key in page)
        {
            var path = ResolvePath(key);
            var info = new FileInfo(path);
            var contentType = ReadContentType(path) ?? "application/octet-stream";
            var lastModified = new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero);
            var etag = ComputeETag(info.Length, lastModified);
            results.Add(new ObjectMetadata(key, info.Length, contentType, etag, lastModified));
        }

        return Task.FromResult(new ListObjectsResult(results, next));
    }

    private static void ValidateKey(string objectKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);
        if (objectKey.StartsWith('/') ||
            objectKey.Contains("..", StringComparison.Ordinal) ||
            objectKey.Contains('\\', StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Object key must be a forward-slash-delimited relative path without '..' segments.",
                nameof(objectKey));
        }
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

    private string ResolvePath(string objectKey)
    {
        var relative = objectKey.Replace('/', Path.DirectorySeparatorChar);
        return Path.GetFullPath(Path.Combine(_options.RootPath, relative));
    }

    private static string ToObjectKey(string root, string path)
    {
        var rel = Path.GetRelativePath(root, path);
        return rel.Replace(Path.DirectorySeparatorChar, '/');
    }

    private static void WriteContentType(string path, string contentType)
        => File.WriteAllText(path + ".contenttype", contentType);

    private static string? ReadContentType(string path)
    {
        var sidecar = path + ".contenttype";
        return File.Exists(sidecar) ? File.ReadAllText(sidecar).Trim() : null;
    }

    private static string ComputeETag(long size, DateTimeOffset lastModified)
        => string.Create(CultureInfo.InvariantCulture, $"\"{size:x}-{lastModified.ToUnixTimeMilliseconds():x}\"");

    private static Uri BuildUri(Uri baseUrl, string objectKey, Dictionary<string, string> query)
    {
        var baseString = baseUrl.AbsoluteUri;
        if (!baseString.EndsWith('/'))
        {
            baseString += "/";
        }

        // Preserve path delimiters while escaping each segment.
        var segments = objectKey.Split('/');
        for (var i = 0; i < segments.Length; i++)
        {
            segments[i] = Uri.EscapeDataString(segments[i]);
        }

        var sb = new StringBuilder(baseString);
        sb.Append(string.Join('/', segments));

        if (query.Count > 0)
        {
            sb.Append('?');
            var first = true;
            foreach (var (k, v) in query)
            {
                if (!first)
                {
                    sb.Append('&');
                }

                sb.Append(Uri.EscapeDataString(k));
                sb.Append('=');
                sb.Append(Uri.EscapeDataString(v));
                first = false;
            }
        }

        return new Uri(sb.ToString(), UriKind.Absolute);
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        Span<char> buf = stackalloc char[name.Length];
        var len = 0;
        foreach (var ch in name)
        {
            if (ch == '"' || ch == '\\' || ch == '\r' || ch == '\n')
            {
                continue;
            }

            var bad = false;
            foreach (var iv in invalid)
            {
                if (ch == iv)
                {
                    bad = true;
                    break;
                }
            }

            if (!bad)
            {
                buf[len++] = ch;
            }
        }

        return len == 0 ? "download" : new string(buf[..len]);
    }

    private static partial class Log
    {
        [LoggerMessage(EventId = 1, Level = LogLevel.Debug,
            Message = "Uploaded object {ObjectKey} ({Size} bytes, {ContentType})")]
        public static partial void Uploaded(ILogger logger, string objectKey, long size, string contentType);

        [LoggerMessage(EventId = 2, Level = LogLevel.Debug, Message = "Deleted object {ObjectKey}")]
        public static partial void Deleted(ILogger logger, string objectKey);
    }
}
