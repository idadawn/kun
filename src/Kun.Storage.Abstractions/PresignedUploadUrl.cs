namespace Kun.Storage;

/// <summary>Presigned PUT URL that a client can use to upload directly to the store.</summary>
/// <param name="Url">The signed URL. Must be used with <c>PUT</c>.</param>
/// <param name="RequiredHeaders">HTTP headers the caller MUST send verbatim for the signature to validate.</param>
/// <param name="ExpiresAt">Absolute expiry time (UTC).</param>
public sealed record PresignedUploadUrl(
    Uri Url,
    IReadOnlyDictionary<string, string> RequiredHeaders,
    DateTimeOffset ExpiresAt);
