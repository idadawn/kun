using System.Collections.Frozen;
using System.Globalization;
using System.Text;

namespace Kun.Storage.Abstractions;

/// <summary>
/// Policy for deciding when browsers must be forced to download (rather than render)
/// a stored object. Active-web MIME types (HTML/SVG/XML/JS/WASM) are served back as
/// <c>attachment</c> so that user-uploaded content cannot execute in the origin of
/// the download domain.
/// </summary>
public static class ContentDispositionPolicy
{
    private static readonly FrozenSet<string> UnsafeMimeTypes = new[]
    {
        "text/html",
        "application/xhtml+xml",
        "image/svg+xml",
        "application/xml",
        "text/xml",
        "application/javascript",
        "text/javascript",
        "application/ecmascript",
        "application/wasm",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Returns <see langword="true"/> if <paramref name="contentType"/> would be rendered
    /// inline by common browsers and therefore should be served as an attachment.
    /// </summary>
    public static bool IsUnsafeForInlineDisplay(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return false;
        }

        // Strip parameters like "; charset=utf-8" before matching.
        var semi = contentType.IndexOf(';', StringComparison.Ordinal);
        var mime = (semi >= 0 ? contentType[..semi] : contentType).Trim();
        return UnsafeMimeTypes.Contains(mime);
    }

    /// <summary>
    /// Returns a <c>Content-Disposition</c> value for the given context. If
    /// <paramref name="contentType"/> is unsafe and <paramref name="currentDisposition"/>
    /// is not already an <c>attachment</c>, replaces it with an attachment disposition.
    /// Otherwise returns <paramref name="currentDisposition"/> unchanged (possibly <see langword="null"/>).
    /// </summary>
    public static string? EnforceAttachment(
        string? contentType,
        string? currentDisposition,
        string? fileName = null)
    {
        if (!IsUnsafeForInlineDisplay(contentType))
        {
            return currentDisposition;
        }

        if (currentDisposition is not null &&
            currentDisposition.TrimStart().StartsWith("attachment", StringComparison.OrdinalIgnoreCase))
        {
            return currentDisposition;
        }

        return BuildAttachmentDisposition(fileName);
    }

    /// <summary>
    /// Builds a canonical <c>attachment</c> disposition. When <paramref name="fileName"/>
    /// is supplied, the name is encoded per RFC 5987 so non-ASCII characters survive.
    /// </summary>
    public static string BuildAttachmentDisposition(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return "attachment";
        }

        // RFC 5987 filename* encoding.
        var encoded = Rfc5987Encode(fileName);
        var asciiFallback = AsciiFallback(fileName);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"attachment; filename=\"{asciiFallback}\"; filename*=UTF-8''{encoded}");
    }

    private static string Rfc5987Encode(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        var sb = new StringBuilder(bytes.Length * 3);
        foreach (var b in bytes)
        {
            var c = (char)b;
            if ((c >= 'a' && c <= 'z') ||
                (c >= 'A' && c <= 'Z') ||
                (c >= '0' && c <= '9') ||
                c == '-' || c == '_' || c == '.' || c == '~')
            {
                sb.Append(c);
            }
            else
            {
                sb.Append('%');
                sb.Append(b.ToString("X2", CultureInfo.InvariantCulture));
            }
        }

        return sb.ToString();
    }

    private static string AsciiFallback(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            sb.Append(c is >= (char)0x20 and < (char)0x7F and not '"' and not '\\' ? c : '_');
        }

        return sb.ToString();
    }
}
