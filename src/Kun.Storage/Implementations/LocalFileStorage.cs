using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Kun.Storage.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kun.Storage.Implementations;

/// <summary>
/// Local filesystem implementation of <see cref="IFileStorage"/> for dev/test use.
/// </summary>
/// <remarks>
/// <para>
/// Metadata (content-type, disposition, user metadata) is persisted in a sidecar
/// file alongside each object with the <c>.kunmeta</c> suffix.
/// </para>
/// <para>
/// <strong>Not for production.</strong> Presigned URLs use the opaque
/// <c>kun-local://</c> scheme signed with HMAC-SHA256; they must be mediated by
/// a dev-only middleware that validates the signature before serving bytes.
/// </para>
/// </remarks>
public sealed partial class LocalFileStorage : IFileStorage
{
    private const string SidecarSuffix = ".kunmeta";

    private static readonly JsonSerializerOptions SidecarJson = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _rootPath;
    private readonly string _rootPrefix;
    private readonly byte[] _signingKey;
    private readonly ILogger<LocalFileStorage> _logger;

    /// <summary>Initializes a new instance.</summary>
    public LocalFileStorage(IOptions<StorageOptions> options, ILogger<LocalFileStorage> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _rootPath = Path.GetFullPath(options.Value.RootPath);
        _rootPrefix = _rootPath.EndsWith(Path.DirectorySeparatorChar)
            ? _rootPath
            : _rootPath + Path.DirectorySeparatorChar;

        _signingKey = DeriveSigningKey(options.Value.SecretKey);
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

        var fullPath = ResolvePath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        await using (var fs = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None,
            bufferSize: 81920, useAsync: true))
        {
            await content.CopyToAsync(fs, ct).ConfigureAwait(false);
        }

        var enforcedDisposition = ContentDispositionPolicy.EnforceAttachment(
            options.ContentType, options.ContentDisposition, options.FileName);

        var sidecar = new SidecarData(
            options.ContentType,
            enforcedDisposition,
            options.CacheControl,
            options.UserMetadata?.ToDictionary(kv => kv.Key, kv => kv.Value));

        await WriteSidecarAsync(fullPath, sidecar, ct).ConfigureAwait(false);
        Log.FilePut(_logger, fullPath);
    }

    /// <inheritdoc />
    public async Task<ObjectDownload> GetObjectAsync(string path, CancellationToken ct = default)
    {
        var fullPath = ResolvePath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"Object not found: {path}", fullPath);
        }

        var metadata = await LoadMetadataAsync(fullPath, ct).ConfigureAwait(false);
        Stream stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 81920, useAsync: true);

        Log.FileGet(_logger, fullPath);
        return new ObjectDownload(stream, metadata);
    }

    /// <inheritdoc />
    public async Task<ObjectMetadata?> HeadObjectAsync(string path, CancellationToken ct = default)
    {
        var fullPath = ResolvePath(path);
        if (!File.Exists(fullPath))
        {
            return null;
        }

        return await LoadMetadataAsync(fullPath, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task DeleteObjectAsync(string path, CancellationToken ct = default)
    {
        var fullPath = ResolvePath(path);

        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
            Log.FileDeleted(_logger, fullPath);
        }

        var sidecar = fullPath + SidecarSuffix;
        if (File.Exists(sidecar))
        {
            File.Delete(sidecar);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<ObjectSummary> ListObjectsAsync(
        string prefix,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        if (!Directory.Exists(_rootPath))
        {
            yield break;
        }

        foreach (var file in Directory.EnumerateFiles(_rootPath, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            if (file.EndsWith(SidecarSuffix, StringComparison.Ordinal))
            {
                continue;
            }

            var relative = Path.GetRelativePath(_rootPath, file).Replace('\\', '/');
            if (!relative.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            var info = new FileInfo(file);
            yield return new ObjectSummary(
                relative,
                info.Length,
                new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero));

            await Task.Yield();
        }
    }

    /// <inheritdoc />
    public Task<PresignedUploadUrl> CreatePresignedUploadUrlAsync(
        string path,
        PresignedUploadRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        _ = ResolvePath(path); // validates the key; avoids signing paths that escape the root

        var expiresAt = DateTimeOffset.UtcNow.Add(request.ExpiresIn);
        var url = BuildStubUrl(path, "upload", expiresAt, request.ContentType, responseContentDisposition: null);

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(request.ContentType))
        {
            headers["Content-Type"] = request.ContentType;
        }

        Log.PresignedUploadUrlGenerated(_logger, path);
        return Task.FromResult(new PresignedUploadUrl(url, "PUT", headers, expiresAt));
    }

    /// <inheritdoc />
    public async Task<PresignedDownloadUrl> CreatePresignedDownloadUrlAsync(
        string path,
        PresignedDownloadRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var fullPath = ResolvePath(path);

        string? disposition = request.ResponseContentDisposition;
        if (disposition is null)
        {
            var metadata = File.Exists(fullPath)
                ? await LoadMetadataAsync(fullPath, ct).ConfigureAwait(false)
                : null;
            var contentType = metadata?.ContentType;
            if (ContentDispositionPolicy.IsUnsafeForInlineDisplay(contentType))
            {
                disposition = ContentDispositionPolicy.EnforceAttachment(
                    contentType, currentDisposition: null, fileName: Path.GetFileName(path));
            }
        }

        var expiresAt = DateTimeOffset.UtcNow.Add(request.ExpiresIn);
        var url = BuildStubUrl(path, "download", expiresAt, contentType: null, responseContentDisposition: disposition);

        Log.PresignedDownloadUrlGenerated(_logger, path);
        return new PresignedDownloadUrl(url, expiresAt);
    }

    // --- helpers --------------------------------------------------------

    private string ResolvePath(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        // Syntactic guard: reject absolute paths and '..' segments before touching the FS.
        if (Path.IsPathRooted(path) || path.Split('/', '\\').Any(s => s == ".."))
        {
            throw new ArgumentException(
                $"Path '{path}' must be relative and must not contain '..' segments.",
                nameof(path));
        }

        var combined = Path.GetFullPath(Path.Combine(_rootPath, path));

        // Semantic guard: even after normalization the result must live under the root.
        if (!combined.StartsWith(_rootPrefix, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(combined, _rootPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"Path '{path}' escapes the storage root.",
                nameof(path));
        }

        return combined;
    }

    private Uri BuildStubUrl(
        string path,
        string op,
        DateTimeOffset expiresAt,
        string? contentType,
        string? responseContentDisposition)
    {
        var encodedPath = Uri.EscapeDataString(path);
        var query = new StringBuilder();
        query.Append("op=").Append(op);
        query.Append("&expires=").Append(expiresAt.ToUnixTimeSeconds());

        if (!string.IsNullOrEmpty(contentType))
        {
            query.Append("&ct=").Append(Uri.EscapeDataString(contentType));
        }

        if (!string.IsNullOrEmpty(responseContentDisposition))
        {
            query.Append("&cd=").Append(Uri.EscapeDataString(responseContentDisposition));
        }

        var payload = $"{encodedPath}?{query}";
        var signature = ComputeSignature(payload);
        return new Uri($"kun-local:///{encodedPath}?{query}&sig={signature}");
    }

    private string ComputeSignature(string payload)
    {
        Span<byte> buffer = stackalloc byte[HMACSHA256.HashSizeInBytes];
        HMACSHA256.HashData(_signingKey, Encoding.UTF8.GetBytes(payload), buffer);
        return Base64UrlEncode(buffer);
    }

    private static string Base64UrlEncode(ReadOnlySpan<byte> bytes)
    {
        var b64 = Convert.ToBase64String(bytes);
        return b64.TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static byte[] DeriveSigningKey(string? configuredKey)
    {
        if (!string.IsNullOrEmpty(configuredKey))
        {
            return SHA256.HashData(Encoding.UTF8.GetBytes(configuredKey));
        }

        // Per-process random key — presigned URLs issued in one run will not
        // validate after restart. Acceptable for local dev.
        return RandomNumberGenerator.GetBytes(32);
    }

    private static async Task<ObjectMetadata> LoadMetadataAsync(string fullPath, CancellationToken ct)
    {
        var sidecar = fullPath + SidecarSuffix;
        var info = new FileInfo(fullPath);
        var lastModified = new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero);

        if (!File.Exists(sidecar))
        {
            return new ObjectMetadata(
                ContentType: "application/octet-stream",
                ContentLength: info.Length,
                ETag: null,
                LastModified: lastModified);
        }

        await using var fs = new FileStream(sidecar, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 4096, useAsync: true);
        var data = await JsonSerializer.DeserializeAsync<SidecarData>(fs, SidecarJson, ct).ConfigureAwait(false)
            ?? new SidecarData("application/octet-stream", null, null, null);

        IReadOnlyDictionary<string, string>? userMeta = data.UserMetadata is { Count: > 0 } ? data.UserMetadata : null;
        return new ObjectMetadata(
            data.ContentType,
            info.Length,
            ETag: null,
            LastModified: lastModified,
            ContentDisposition: data.ContentDisposition,
            UserMetadata: userMeta);
    }

    private static async Task WriteSidecarAsync(string fullPath, SidecarData data, CancellationToken ct)
    {
        var sidecar = fullPath + SidecarSuffix;
        await using var fs = new FileStream(sidecar, FileMode.Create, FileAccess.Write, FileShare.None,
            bufferSize: 4096, useAsync: true);
        await JsonSerializer.SerializeAsync(fs, data, SidecarJson, ct).ConfigureAwait(false);
    }

    private sealed record SidecarData(
        string ContentType,
        string? ContentDisposition,
        string? CacheControl,
        Dictionary<string, string>? UserMetadata);

    private static partial class Log
    {
        [LoggerMessage(LogLevel.Debug, "Put file at {Path}")]
        public static partial void FilePut(ILogger logger, string path);

        [LoggerMessage(LogLevel.Debug, "Get file from {Path}")]
        public static partial void FileGet(ILogger logger, string path);

        [LoggerMessage(LogLevel.Debug, "Deleted file at {Path}")]
        public static partial void FileDeleted(ILogger logger, string path);

        [LoggerMessage(LogLevel.Debug, "Generated presigned upload stub for {Path}")]
        public static partial void PresignedUploadUrlGenerated(ILogger logger, string path);

        [LoggerMessage(LogLevel.Debug, "Generated presigned download stub for {Path}")]
        public static partial void PresignedDownloadUrlGenerated(ILogger logger, string path);
    }
}
