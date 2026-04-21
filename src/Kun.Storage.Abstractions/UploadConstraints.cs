namespace Kun.Storage;

/// <summary>
/// Upload policy applied by <see cref="IFileStorage.UploadAsync"/> and encoded into
/// <see cref="IFileStorage.GetPresignedUploadAsync"/> signatures.
/// </summary>
/// <param name="MaxBytes">Maximum payload size in bytes. <c>null</c> means no explicit limit.</param>
/// <param name="AllowedContentTypes">
/// Whitelist of permitted MIME types. <c>null</c> or empty means any content type is accepted.
/// Comparison is case-insensitive.
/// </param>
public sealed record UploadConstraints(
    long? MaxBytes = null,
    IReadOnlyCollection<string>? AllowedContentTypes = null);
