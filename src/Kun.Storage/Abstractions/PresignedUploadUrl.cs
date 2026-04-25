namespace Kun.Storage.Abstractions;

/// <summary>
/// Result of
/// <see cref="IFileStorage.CreatePresignedUploadUrlAsync(string, PresignedUploadRequest, CancellationToken)"/>.
/// </summary>
/// <param name="Url">Absolute URL the client should call to upload the object.</param>
/// <param name="HttpMethod">HTTP method to use (usually <c>PUT</c>).</param>
/// <param name="RequiredHeaders">
/// Headers the client MUST send with the upload request. Signatures are computed over these values,
/// so omitting or altering them will cause the backend to reject the upload.
/// </param>
/// <param name="ExpiresAt">Absolute UTC time at which the URL stops being accepted.</param>
public sealed record PresignedUploadUrl(
    Uri Url,
    string HttpMethod,
    IReadOnlyDictionary<string, string> RequiredHeaders,
    DateTimeOffset ExpiresAt);
