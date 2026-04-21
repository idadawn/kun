namespace Kun.Storage.Abstractions;

/// <summary>
/// Abstraction for file storage operations supporting multiple backends (local FS, S3, MinIO).
/// </summary>
public interface IFileStorage
{
    /// <summary>Uploads a file to the given path.</summary>
    Task PutAsync(string path, Stream content, string contentType, CancellationToken ct = default);

    /// <summary>Downloads a file from the given path.</summary>
    Task<Stream> GetAsync(string path, CancellationToken ct = default);

    /// <summary>Deletes a file at the given path. No-op if the file does not exist.</summary>
    Task DeleteAsync(string path, CancellationToken ct = default);

    /// <summary>Returns whether a file exists at the given path.</summary>
    Task<bool> ExistsAsync(string path, CancellationToken ct = default);

    /// <summary>Generates a pre-signed URL for temporary access to a file.</summary>
    Task<Uri> GetPresignedUrlAsync(string path, TimeSpan expires, CancellationToken ct = default);
}
