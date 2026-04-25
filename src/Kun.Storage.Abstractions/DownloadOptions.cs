namespace Kun.Storage;

/// <summary>
/// Per-call download behavior hints.
/// </summary>
/// <param name="ForceDownload">
/// When <c>true</c>, the URL MUST carry <c>Content-Disposition: attachment</c> regardless of content type.
/// Implementations MUST also force attachment when the content type is in
/// <see cref="ActiveWebContentTypes.Value"/>, even if this flag is <c>false</c>.
/// </param>
/// <param name="DownloadFileName">
/// Optional file name used in the <c>filename</c> parameter of <c>Content-Disposition</c>.
/// Implementations sanitize the value to exclude control characters and path separators.
/// </param>
/// <param name="ResponseHeaders">
/// Additional response headers the store should return (e.g. <c>Cache-Control</c>).
/// Implementations may drop headers they cannot sign.
/// </param>
public sealed record DownloadOptions(
    bool ForceDownload = false,
    string? DownloadFileName = null,
    IReadOnlyDictionary<string, string>? ResponseHeaders = null);
