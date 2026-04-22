namespace Kun.Storage.Implementations;

/// <summary>
/// Shared policy that decides whether a given content type must be served
/// with <c>Content-Disposition: attachment</c>. Active web types
/// (HTML / XML / SVG / legacy Flash) are flagged here so both the S3 and
/// local-filesystem backends apply the same XSS-hardening rule.
/// </summary>
internal static class ContentDispositionPolicy
{
    private static readonly HashSet<string> ActiveWebTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "text/html",
        "text/xml",
        "application/xhtml+xml",
        "application/xml",
        "image/svg+xml",
        "application/x-shockwave-flash",
    };

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="contentType"/>
    /// identifies an active-web type that can execute in a browser if
    /// served inline, and therefore must be forced to download.
    /// </summary>
    public static bool RequiresAttachment(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return false;
        }

        // Strip parameters ("; charset=utf-8") before lookup.
        var semi = contentType.IndexOf(';', StringComparison.Ordinal);
        var bare = semi < 0 ? contentType : contentType[..semi];
        return ActiveWebTypes.Contains(bare.Trim());
    }

    /// <summary>
    /// Returns a canonical <c>Content-Disposition</c> header value for
    /// <paramref name="objectKey"/>, or <see langword="null"/> when
    /// <paramref name="contentType"/> is safe for inline rendering.
    /// The <c>filename</c> is the last path segment of the object key,
    /// URL-encoded to stop header injection.
    /// </summary>
    public static string? BuildHeaderValue(string objectKey, string? contentType)
    {
        if (!RequiresAttachment(contentType))
        {
            return null;
        }

        ArgumentException.ThrowIfNullOrEmpty(objectKey);
        var slash = objectKey.LastIndexOf('/');
        var filename = slash < 0 ? objectKey : objectKey[(slash + 1)..];
        var encoded = Uri.EscapeDataString(filename);
        return $"attachment; filename=\"{encoded}\"";
    }
}
