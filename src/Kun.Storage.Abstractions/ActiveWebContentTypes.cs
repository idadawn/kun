using System.Collections.Frozen;

namespace Kun.Storage;

/// <summary>
/// The canonical set of MIME types that browsers render as active web content —
/// i.e. payloads that could execute script or navigate against the serving origin
/// if opened inline. An <see cref="IFileStorage"/> implementation MUST force
/// <c>Content-Disposition: attachment</c> on presigned download URLs whose
/// <see cref="ObjectMetadata.ContentType"/> is contained in this set, regardless
/// of whether the caller requested <see cref="DownloadOptions.ForceDownload"/>.
/// </summary>
/// <remarks>
/// The four entries are fixed by M1 of the Sprint-1 gap-closure plan:
/// <c>text/html</c>, <c>image/svg+xml</c>, <c>application/xhtml+xml</c>, <c>application/xml</c>.
/// Comparison is case-insensitive.
/// </remarks>
public static class ActiveWebContentTypes
{
    /// <summary>
    /// The exact four MIME types for which presigned download URLs are forced to
    /// <c>Content-Disposition: attachment</c>.
    /// </summary>
    public static readonly FrozenSet<string> Value = FrozenSet.ToFrozenSet(
        new[]
        {
            "text/html",
            "image/svg+xml",
            "application/xhtml+xml",
            "application/xml",
        },
        StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Returns <c>true</c> when <paramref name="contentType"/> is one of the four
    /// canonical active web content types.
    /// </summary>
    /// <param name="contentType">The MIME type reported by the stored object's metadata.</param>
    public static bool Contains(string? contentType)
        => !string.IsNullOrWhiteSpace(contentType) && Value.Contains(contentType!);
}
