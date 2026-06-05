namespace Kun.Storage.Oss;

/// <summary>Configures <see cref="OssFileStorage"/>.</summary>
public sealed class OssStorageOptions
{
    /// <summary>
    /// OSS service endpoint, e.g. <c>https://oss-cn-hangzhou.aliyuncs.com</c> (public) or
    /// <c>https://oss-cn-hangzhou-internal.aliyuncs.com</c> (intranet — no public egress cost).
    /// Required.
    /// </summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>Target bucket. Required.</summary>
    public string BucketName { get; set; } = string.Empty;

    /// <summary>OSS AccessKey id. Required — OSS has no ambient credential resolution chain.</summary>
    public string AccessKeyId { get; set; } = string.Empty;

    /// <summary>OSS AccessKey secret paired with <see cref="AccessKeyId"/>. Required.</summary>
    public string AccessKeySecret { get; set; } = string.Empty;

    /// <summary>
    /// Default presigned-URL lifetime when the caller does not specify one. OSS caps a single
    /// presigned URL at 7 days.
    /// </summary>
    public TimeSpan DefaultPresignedUrlLifetime { get; set; } = TimeSpan.FromMinutes(15);
}
