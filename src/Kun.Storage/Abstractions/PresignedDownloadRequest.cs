namespace Kun.Storage.Abstractions;

/// <summary>
/// Parameters for
/// <see cref="IFileStorage.CreatePresignedDownloadUrlAsync(string, PresignedDownloadRequest, CancellationToken)"/>.
/// </summary>
/// <param name="ExpiresIn">URL lifetime. Keep short for public-facing download links.</param>
/// <param name="ResponseContentDisposition">
/// Optional override for the response <c>Content-Disposition</c>. When not specified,
/// the backend still enforces <see cref="ContentDispositionPolicy"/>: active-web MIME types
/// (HTML/SVG/XML/JS/WASM) are served as <c>attachment</c> regardless of stored disposition.
/// </param>
public sealed record PresignedDownloadRequest(
    TimeSpan ExpiresIn,
    string? ResponseContentDisposition = null);
