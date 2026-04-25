namespace Kun.Sandbox.Abstractions;

/// <summary>
/// File sub-API of an <see cref="ISandboxHandle"/>. Maps to the E2B v2
/// <c>GET/POST/DELETE /v2/sandboxes/{id}/files?path=...</c> endpoints and
/// <c>GET /v2/sandboxes/{id}/files/list?path=...</c>.
/// </summary>
public interface IFiles
{
    /// <summary>Reads a file out of the sandbox as raw bytes.</summary>
    /// <param name="path">Absolute path inside the sandbox. Paths must not contain <c>..</c> and must live under <c>/mnt/user-data/</c> (enforced by the Supervisor backend).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<byte[]> ReadAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>Writes <paramref name="content"/> to <paramref name="path"/> inside the sandbox.</summary>
    Task WriteAsync(string path, byte[] content, CancellationToken cancellationToken = default);

    /// <summary>Lists directory entries at <paramref name="path"/>.</summary>
    /// <param name="path">Directory path inside the sandbox.</param>
    /// <param name="recursive">When <see langword="true"/>, walk sub-directories.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<FileEntry>> ListAsync(
        string path,
        bool recursive = false,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes a single file at <paramref name="path"/>.</summary>
    Task DeleteAsync(string path, CancellationToken cancellationToken = default);
}
