namespace Kun.Storage.Abstractions;

/// <summary>Configuration options for Kun.Storage.</summary>
public sealed class StorageOptions
{
    /// <summary>The storage provider to use.</summary>
    public StorageProvider Provider { get; set; } = StorageProvider.Local;

    /// <summary>Root directory for local filesystem storage. Defaults to "./storage".</summary>
    public string RootPath { get; set; } = "./storage";

    /// <summary>S3/MinIO bucket name.</summary>
    public string BucketName { get; set; } = string.Empty;

    /// <summary>Custom S3-compatible endpoint (e.g. MinIO). Leave empty for AWS S3.</summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>Access key for S3/MinIO authentication.</summary>
    public string AccessKey { get; set; } = string.Empty;

    /// <summary>Secret key for S3/MinIO authentication.</summary>
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>AWS region for S3.</summary>
    public string Region { get; set; } = string.Empty;
}

/// <summary>Supported storage providers.</summary>
public enum StorageProvider
{
    /// <summary>Local filesystem.</summary>
    Local,

    /// <summary>Amazon S3 or S3-compatible (MinIO).</summary>
    S3,
}
