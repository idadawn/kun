namespace Kun.Storage.Abstractions;

/// <summary>Metadata describing a stored object.</summary>
/// <param name="ContentType">MIME type declared at upload time.</param>
/// <param name="ContentLength">Object size in bytes.</param>
/// <param name="ETag">Backend-assigned entity tag, if any. Absent for backends that do not emit ETags.</param>
/// <param name="LastModified">UTC timestamp of the last successful write.</param>
/// <param name="ContentDisposition">Stored <c>Content-Disposition</c>, including any attachment enforcement applied by the backend.</param>
/// <param name="UserMetadata">Arbitrary key/value pairs supplied at upload time.</param>
public sealed record ObjectMetadata(
    string ContentType,
    long ContentLength,
    string? ETag,
    DateTimeOffset LastModified,
    string? ContentDisposition = null,
    IReadOnlyDictionary<string, string>? UserMetadata = null);
