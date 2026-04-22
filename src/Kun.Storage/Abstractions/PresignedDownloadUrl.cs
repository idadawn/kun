namespace Kun.Storage.Abstractions;

/// <summary>
/// Short-lived URL a client can <c>GET</c> to download a file directly
/// from the storage backend, bypassing the application.
/// </summary>
/// <param name="Url">The signed download URL.</param>
/// <param name="Expiry">UTC timestamp at which <paramref name="Url"/> stops being valid.</param>
/// <param name="ObjectKey">The object key being exposed.</param>
public sealed record PresignedDownloadUrl(
    Uri Url,
    DateTimeOffset Expiry,
    string ObjectKey);
