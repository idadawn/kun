namespace Kun.Storage.Abstractions;

/// <summary>Options supplied when uploading an object.</summary>
/// <param name="ContentType">MIME type. Required.</param>
/// <param name="ContentDisposition">
/// Optional explicit <c>Content-Disposition</c>. If <see langword="null"/> and
/// <paramref name="ContentType"/> is in the unsafe-for-inline-display list,
/// implementations MUST populate this with an <c>attachment</c> disposition.
/// </param>
/// <param name="CacheControl">Optional <c>Cache-Control</c> header value.</param>
/// <param name="FileName">Optional original file name; used to enrich the enforced attachment disposition.</param>
/// <param name="UserMetadata">Arbitrary user metadata. Keys should be lowercase ASCII.</param>
public sealed record PutObjectOptions(
    string ContentType,
    string? ContentDisposition = null,
    string? CacheControl = null,
    string? FileName = null,
    IReadOnlyDictionary<string, string>? UserMetadata = null);
