using Kun.Storage.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kun.Storage.Implementations;

/// <summary>
/// Local filesystem implementation of <see cref="IFileStorage"/>.
/// </summary>
public sealed partial class LocalFileStorage : IFileStorage
{
    private readonly string _rootPath;
    private readonly ILogger<LocalFileStorage> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="LocalFileStorage"/> class.
    /// </summary>
    /// <param name="options">Storage options; the root path is resolved to an absolute path.</param>
    /// <param name="logger">Logger for file operations.</param>
    public LocalFileStorage(IOptions<StorageOptions> options, ILogger<LocalFileStorage> logger)
    {
        _rootPath = Path.GetFullPath(options.Value.RootPath);
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task PutAsync(string path, Stream content, string contentType, CancellationToken ct = default)
    {
        var fullPath = GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        await using var fs = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None,
            bufferSize: 81920, useAsync: true);
        await content.CopyToAsync(fs, ct);

        Log.FilePut(_logger, fullPath);
    }

    /// <inheritdoc />
    public Task<Stream> GetAsync(string path, CancellationToken ct = default)
    {
        var fullPath = GetFullPath(path);
        Stream stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 81920, useAsync: true);

        Log.FileGet(_logger, fullPath);
        return Task.FromResult(stream);
    }

    /// <inheritdoc />
    public Task DeleteAsync(string path, CancellationToken ct = default)
    {
        var fullPath = GetFullPath(path);

        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
            Log.FileDeleted(_logger, fullPath);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool> ExistsAsync(string path, CancellationToken ct = default)
    {
        var fullPath = GetFullPath(path);
        return Task.FromResult(File.Exists(fullPath));
    }

    /// <inheritdoc />
    public Task<Uri> GetPresignedUrlAsync(string path, TimeSpan expires, CancellationToken ct = default)
    {
        throw new NotSupportedException("Pre-signed URLs are not supported by local filesystem storage.");
    }

    private string GetFullPath(string path) =>
        Path.GetFullPath(Path.Combine(_rootPath, path));

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
