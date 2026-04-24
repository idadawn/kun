namespace Kun.Storage.Abstractions;

/// <summary>
/// Parameters for
/// <see cref="IFileStorage.CreatePresignedUploadUrlAsync(string, PresignedUploadRequest, CancellationToken)"/>.
/// </summary>
/// <param name="ExpiresIn">URL lifetime. Should be kept short (minutes, not hours).</param>
/// <param name="ContentType">
/// Expected content type. If the backend supports signing the Content-Type header
/// (S3 does), it will be bound into the signature.
/// </param>
/// <param name="ContentLengthMin">Optional lower bound on the object size.</param>
/// <param name="ContentLengthMax">Optional upper bound on the object size.</param>
public sealed record PresignedUploadRequest(
    TimeSpan ExpiresIn,
    string? ContentType = null,
    long? ContentLengthMin = null,
    long? ContentLengthMax = null);
