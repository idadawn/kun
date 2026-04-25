namespace Kun.Storage.Local;

/// <summary>
/// Configures <see cref="LocalFileSystemStorage"/>.
/// </summary>
public sealed class LocalFileStorageOptions
{
    /// <summary>
    /// Root directory on the local filesystem where objects are stored. Absolute path.
    /// The directory is created if it does not exist at first use.
    /// </summary>
    public string RootPath { get; set; } = string.Empty;

    /// <summary>
    /// Base URL prefix used to build presigned URLs. The host app must expose an HTTP
    /// endpoint at this prefix that streams files from <see cref="RootPath"/>. The
    /// final URL shape is <c>{PublicBaseUrl}/{objectKey}?expires=…&amp;disposition=…</c>.
    /// </summary>
    public Uri PublicBaseUrl { get; set; } = new Uri("http://localhost/storage/", UriKind.Absolute);

    /// <summary>
    /// Default validity window applied when the caller does not specify <c>expiresIn</c>.
    /// </summary>
    public TimeSpan DefaultPresignedUrlLifetime { get; set; } = TimeSpan.FromMinutes(15);
}
