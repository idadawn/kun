namespace Kun.Storage.Abstractions;

/// <summary>
/// Short-lived URL a client can <c>PUT</c> to in order to upload a file
/// directly to the storage backend, bypassing the application.
/// </summary>
/// <param name="Url">The signed upload URL.</param>
/// <param name="Expiry">UTC timestamp at which <paramref name="Url"/> stops being valid.</param>
/// <param name="ObjectKey">The object key under which the upload will land.</param>
/// <param name="RequiredContentType">
/// When non-<see langword="null"/>, the client MUST send a matching
/// <c>Content-Type</c> header or the upload will be rejected by the
/// backend. <see langword="null"/> means the backend will accept any
/// content type.
/// </param>
public sealed record PresignedUploadUrl(
    Uri Url,
    DateTimeOffset Expiry,
    string ObjectKey,
    string? RequiredContentType);
