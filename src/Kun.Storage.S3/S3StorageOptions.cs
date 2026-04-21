namespace Kun.Storage.S3;

/// <summary>Configures <see cref="S3FileStorage"/>.</summary>
public sealed class S3StorageOptions
{
    /// <summary>Target bucket. Required.</summary>
    public string BucketName { get; set; } = string.Empty;

    /// <summary>
    /// Optional service URL override for S3-compatible stores (MinIO, LocalStack, Ceph).
    /// When <c>null</c>, the SDK resolves the regional AWS endpoint.
    /// </summary>
    public Uri? ServiceUrl { get; set; }

    /// <summary>AWS region (e.g. <c>us-east-1</c>). Required when <see cref="ServiceUrl"/> is not set.</summary>
    public string? Region { get; set; }

    /// <summary>
    /// When <c>true</c>, URLs use path-style addressing (<c>https://host/bucket/key</c>)
    /// instead of virtual-host style (<c>https://bucket.host/key</c>). Required for MinIO
    /// and LocalStack; incompatible with some new AWS buckets.
    /// </summary>
    public bool ForcePathStyle { get; set; }

    /// <summary>Default presigned-URL lifetime when the caller does not specify one.</summary>
    public TimeSpan DefaultPresignedUrlLifetime { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Optional static access key. When both <see cref="AccessKey"/> and <see cref="SecretKey"/>
    /// are non-null, they override the SDK's default credential resolution chain.
    /// </summary>
    public string? AccessKey { get; set; }

    /// <summary>Optional static secret key paired with <see cref="AccessKey"/>.</summary>
    public string? SecretKey { get; set; }
}
