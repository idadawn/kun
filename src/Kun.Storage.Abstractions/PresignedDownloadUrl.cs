namespace Kun.Storage;

/// <summary>Presigned GET URL that lets a client download the object directly from the store.</summary>
/// <param name="Url">The signed URL. Should be used with <c>GET</c>.</param>
/// <param name="ResponseHeaders">Response headers the store will return (e.g. <c>Content-Disposition</c>).</param>
/// <param name="ExpiresAt">Absolute expiry time (UTC).</param>
/// <param name="ContentDispositionForced">
/// <c>true</c> when the implementation applied <c>Content-Disposition: attachment</c> automatically
/// because the object's content type is an active web content type (see
/// <see cref="ActiveWebContentTypes.Value"/>).
/// </param>
public sealed record PresignedDownloadUrl(
    Uri Url,
    IReadOnlyDictionary<string, string> ResponseHeaders,
    DateTimeOffset ExpiresAt,
    bool ContentDispositionForced);
