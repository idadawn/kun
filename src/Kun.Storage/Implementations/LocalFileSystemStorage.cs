using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Kun.Storage.Abstractions;
using Kun.Storage.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kun.Storage.Implementations;

/// <summary>
/// Local-filesystem implementation of <see cref="IFileStorage"/>. Intended
/// for development and tests only — presigned URLs are not supported and
/// Content-Disposition enforcement is the responsibility of the HTTP
/// layer that serves files from disk (this class only records the content
/// type sidecar; it does not serve HTTP).
/// </summary>
/// <remarks>
/// <para>
/// Content type and upload timestamp are persisted in a sibling
/// <c>{key}.kun-meta</c> file in a simple <c>content-type=…</c> /
/// <c>sha256=…</c> / <c>size=…</c> / <c>last-modified=…</c> format.
/// The plain-text sidecar avoids JSON serialization dependencies and
/// keeps the module AOT-friendly.
/// </para>
/// <para>
/// Object keys must not begin with <c>/</c>, may not contain <c>..</c>
/// segments, and are translated verbatim to relative paths under the
/// configured <see cref="LocalStorageOptions.RootPath"/>. Escaping the
/// root directory throws <see cref="UnauthorizedAccessException"/>.
/// </para>
/// </remarks>
public sealed partial class LocalFileSystemStorage : IFileStorage
{
    private const string MetaSuffix = ".kun-meta";

    private readonly string _rootPath;
    private readonly ILogger<LocalFileSystemStorage> _logger;

    /// <summary>Creates a new <see cref="LocalFileSystemStorage"/>.</summary>
    public LocalFileSystemStorage(IOptions<LocalStorageOptions> options, ILogger<LocalFileSystemStorage> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _rootPath = Path.GetFullPath(options.Value.RootPath);
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

        var fullPath = ResolveBodyPath(objectKey);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        long size;
        string sha256;
        using (var hash = SHA256.Create())
        await using (var fs = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None,
            bufferSize: 81920, useAsync: true))
        await using (var sink = new CryptoStream(fs, hash, CryptoStreamMode.Write, leaveOpen: true))
        {
            await content.CopyToAsync(sink, ct).ConfigureAwait(false);
            await sink.FlushFinalBlockAsync(ct).ConfigureAwait(false);
            size = fs.Position;
            sha256 = Convert.ToHexStringLower(hash.Hash!);
        }

        var lastModified = DateTimeOffset.UtcNow;
        await WriteMetadataAsync(objectKey, contentType, sha256, size, lastModified, ct).ConfigureAwait(false);

        Log.ObjectPut(_logger, fullPath);
        return new ObjectMetadata(objectKey, size, contentType, sha256, lastModified);
    }

    /// <inheritdoc />
    public Task<Stream> GetObjectAsync(string objectKey, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);
        var fullPath = ResolveBodyPath(objectKey);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"Object '{objectKey}' not found.", objectKey);
        }

        Stream stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 81920, useAsync: true);
        Log.ObjectGet(_logger, fullPath);
        return Task.FromResult(stream);
    }

    /// <inheritdoc />
    public async Task<ObjectMetadata?> HeadObjectAsync(string objectKey, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);
        var fullPath = ResolveBodyPath(objectKey);
        if (!File.Exists(fullPath))
        {
            return null;
        }

        var info = new FileInfo(fullPath);
        var meta = await ReadMetadataAsync(objectKey, ct).ConfigureAwait(false);
        return new ObjectMetadata(
            ObjectKey: objectKey,
            Size: info.Length,
            ContentType: meta?.ContentType,
            Sha256: meta?.Sha256,
            LastModified: meta?.LastModified ?? new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero));
    }

    /// <inheritdoc />
    public Task DeleteObjectAsync(string objectKey, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);
        var fullPath = ResolveBodyPath(objectKey);
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
            Log.ObjectDeleted(_logger, fullPath);
        }

        var metaPath = fullPath + MetaSuffix;
        if (File.Exists(metaPath))
        {
            File.Delete(metaPath);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<ObjectMetadata> ListObjectsAsync(
        string? prefix = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (!Directory.Exists(_rootPath))
        {
            yield break;
        }

        foreach (var fullPath in Directory.EnumerateFiles(_rootPath, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            if (fullPath.EndsWith(MetaSuffix, StringComparison.Ordinal))
            {
                continue;
            }

            var relative = Path.GetRelativePath(_rootPath, fullPath).Replace(Path.DirectorySeparatorChar, '/');
            if (!string.IsNullOrEmpty(prefix) && !relative.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            var meta = await ReadMetadataAsync(relative, ct).ConfigureAwait(false);
            var info = new FileInfo(fullPath);
            yield return new ObjectMetadata(
                ObjectKey: relative,
                Size: info.Length,
                ContentType: meta?.ContentType,
                Sha256: meta?.Sha256,
                LastModified: meta?.LastModified ?? new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero));
        }
    }

    /// <inheritdoc />
    public Task<PresignedUploadUrl> CreatePresignedUploadUrlAsync(
        string objectKey,
        TimeSpan expiresIn,
        string? contentType = null,
        CancellationToken ct = default)
        => throw new NotSupportedException("Presigned URLs are not supported for local filesystem storage.");

    /// <inheritdoc />
    public Task<PresignedDownloadUrl> CreatePresignedDownloadUrlAsync(
        string objectKey,
        TimeSpan expiresIn,
        CancellationToken ct = default)
        => throw new NotSupportedException("Presigned URLs are not supported for local filesystem storage.");

    private string ResolveBodyPath(string objectKey)
    {
        if (objectKey.StartsWith('/') || objectKey.StartsWith('\\') || objectKey.Contains(".."))
        {
            throw new UnauthorizedAccessException($"Object key '{objectKey}' escapes the storage root.");
        }
        var full = Path.GetFullPath(Path.Combine(_rootPath, objectKey));
        if (!full.StartsWith(_rootPath, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException($"Object key '{objectKey}' escapes the storage root.");
        }
        return full;
    }

    private async Task WriteMetadataAsync(string objectKey, string contentType, string sha256, long size, DateTimeOffset lastModified, CancellationToken ct)
    {
        var metaPath = ResolveBodyPath(objectKey) + MetaSuffix;
        var lines = new[]
        {
            $"content-type={contentType}",
            $"sha256={sha256}",
            $"size={size}",
            $"last-modified={lastModified:O}",
        };
        await File.WriteAllLinesAsync(metaPath, lines, ct).ConfigureAwait(false);
    }

    private async Task<LocalObjectMeta?> ReadMetadataAsync(string objectKey, CancellationToken ct)
    {
        var metaPath = ResolveBodyPath(objectKey) + MetaSuffix;
        if (!File.Exists(metaPath))
        {
            return null;
        }

        var lines = await File.ReadAllLinesAsync(metaPath, ct).ConfigureAwait(false);
        string? contentType = null;
        string? sha256 = null;
        long size = 0;
        DateTimeOffset lastModified = default;
        foreach (var line in lines)
        {
            var eq = line.IndexOf('=', StringComparison.Ordinal);
            if (eq < 0)
            {
                continue;
            }
            var key = line[..eq];
            var value = line[(eq + 1)..];
            switch (key)
            {
                case "content-type": contentType = value; break;
                case "sha256": sha256 = value; break;
                case "size": _ = long.TryParse(value, out size); break;
                case "last-modified": _ = DateTimeOffset.TryParse(value, out lastModified); break;
                default: break;
            }
        }
        return new LocalObjectMeta(contentType, sha256, size, lastModified);
    }

    private readonly record struct LocalObjectMeta(string? ContentType, string? Sha256, long Size, DateTimeOffset LastModified);

    private static partial class Log
    {
        [LoggerMessage(LogLevel.Debug, "Put object at {Path}")]
        public static partial void ObjectPut(ILogger logger, string path);

        [LoggerMessage(LogLevel.Debug, "Get object from {Path}")]
        public static partial void ObjectGet(ILogger logger, string path);

        [LoggerMessage(LogLevel.Debug, "Deleted object at {Path}")]
        public static partial void ObjectDeleted(ILogger logger, string path);
    }
}
