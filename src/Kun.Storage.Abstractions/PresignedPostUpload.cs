namespace Kun.Storage;

/// <summary>
/// Presigned POST descriptor for direct browser/client uploads. Unlike the PUT-based
/// <see cref="PresignedUploadUrl"/>, the signed policy baked into the form fields
/// enforces <see cref="UploadConstraints.MaxBytes"/>: S3 itself rejects oversized bodies
/// with HTTP 400 before any bytes land in the bucket.
/// </summary>
/// <param name="Url">Endpoint to POST a multipart/form-data body to.</param>
/// <param name="FormFields">Form fields that MUST appear verbatim in the multipart body
/// before the <c>file</c> field (policy, x-amz-signature, x-amz-credential, x-amz-date,
/// x-amz-algorithm, key, Content-Type, etc.). Order matters — callers should iterate the
/// dictionary and append each key/value in order, then append the file bytes last.</param>
/// <param name="ExpiresAt">Absolute expiry (UTC) of the signed policy.</param>
public sealed record PresignedPostUpload(
    Uri Url,
    IReadOnlyDictionary<string, string> FormFields,
    DateTimeOffset ExpiresAt);
