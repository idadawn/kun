using System.Runtime.CompilerServices;
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
/// Metadata (content-type, disposition, user metadata) is persisted in a sidecar
/// file alongside each object with the <c>.kunmeta</c> suffix. Presigned URLs are
/// not fully wired in this commit — they land in the local-impl hardening task.
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
    private readonly ILogger<LocalFileStorage> _logger;

    /// <summary>Initializes a new instance.</summary>
    public LocalFileStorage(IOptions<StorageOptions> options, ILogger<LocalFileStorage> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _rootPath = Path.GetFullPath(options.Value.RootPath);
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

        await WriteSidecarAsync(fullPath, new SidecarData(
            options.ContentType,
            options.ContentDisposition,
            options.CacheControl,
            options.UserMetadata?.ToDictionary(kv => kv.Key, kv => kv.Value)), ct).ConfigureAwait(false);

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

            // Keep the method async without actually awaiting I/O inside the loop body;
            // yield control occasionally so callers can cancel cooperatively.
            await Task.Yield();
        }
    }

    /// <inheritdoc />
    public Task<PresignedUploadUrl> CreatePresignedUploadUrlAsync(
        string path,
        PresignedUploadRequest request,
        CancellationToken ct = default) =>
        throw new NotSupportedException(
            "Presigned upload URLs are not yet implemented for LocalFileStorage. See Kun.Storage roadmap.");

    /// <inheritdoc />
    public Task<PresignedDownloadUrl> CreatePresignedDownloadUrlAsync(
        string path,
        PresignedDownloadRequest request,
        CancellationToken ct = default) =>
        throw new NotSupportedException(
            "Presigned download URLs are not yet implemented for LocalFileStorage. See Kun.Storage roadmap.");

    private string ResolvePath(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return Path.GetFullPath(Path.Combine(_rootPath, path));
    }

    private async Task<ObjectMetadata> LoadMetadataAsync(string fullPath, CancellationToken ct)
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
                LastModified: lastModified,
                ContentDisposition: null,
                UserMetadata: null);
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
    }
}
