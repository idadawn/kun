namespace Kun.Storage.Abstractions;

/// <summary>
/// Result of
/// <see cref="IFileStorage.CreatePresignedDownloadUrlAsync(string, PresignedDownloadRequest, CancellationToken)"/>.
/// </summary>
/// <param name="Url">Absolute URL the client can <c>GET</c> to download the object.</param>
/// <param name="ExpiresAt">Absolute UTC time at which the URL stops being accepted.</param>
public sealed record PresignedDownloadUrl(Uri Url, DateTimeOffset ExpiresAt);
