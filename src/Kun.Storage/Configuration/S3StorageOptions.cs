namespace Kun.Storage.Configuration;

/// <summary>Options for <c>S3FileStorage</c> (Amazon S3 or MinIO-compatible).</summary>
public sealed class S3StorageOptions
{
    /// <summary>S3 / MinIO bucket name. Required.</summary>
    public string BucketName { get; set; } = string.Empty;

    /// <summary>
    /// Custom S3-compatible endpoint (e.g. <c>http://minio:9000</c>). Leave
    /// unset when targeting AWS; the AWS SDK picks the regional endpoint
    /// from <see cref="Region"/>.
    /// </summary>
    public string? Endpoint { get; set; }

    /// <summary>AWS region; required when <see cref="Endpoint"/> is unset.</summary>
    public string? Region { get; set; }

    /// <summary>Access key for S3 / MinIO authentication.</summary>
    public string AccessKey { get; set; } = string.Empty;

    /// <summary>Secret key for S3 / MinIO authentication.</summary>
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>
    /// When <see langword="true"/> (default for custom endpoints), uses
    /// path-style addressing (<c>http://host/bucket/key</c>) instead of
    /// virtual-hosted style (<c>http://bucket.host/key</c>). MinIO needs
    /// path-style; most managed AWS buckets accept either.
    /// </summary>
    public bool ForcePathStyle { get; set; } = true;
}
