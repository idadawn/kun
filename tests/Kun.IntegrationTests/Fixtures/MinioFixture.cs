using System.Net;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Testcontainers.Minio;
using Xunit;

namespace Kun.IntegrationTests.Fixtures;

/// <summary>
/// Per-class fixture that starts a MinIO container, provisions a test bucket,
/// and exposes an <see cref="IAmazonS3"/> factory configured for the container.
/// Skips automatically when Docker is unavailable.
/// </summary>
public sealed class MinioFixture : IAsyncLifetime
{
    private const string Username = "minioadmin";
    private const string Password = "minioadmin";

    private MinioContainer? _container;

    /// <summary>HTTP endpoint to reach the running MinIO instance.</summary>
    public string Endpoint { get; private set; } = string.Empty;

    /// <summary>Access key (MinIO root user).</summary>
    public string AccessKey => Username;

    /// <summary>Secret key (MinIO root password).</summary>
    public string SecretKey => Password;

    /// <summary>Bucket provisioned for the fixture lifetime.</summary>
    public string BucketName { get; } = $"kun-test-{Guid.NewGuid():N}";

    /// <inheritdoc/>
    public async ValueTask InitializeAsync()
    {
        try
        {
            _container = new MinioBuilder()
                .WithImage("minio/minio:latest")
                .WithUsername(Username)
                .WithPassword(Password)
                .Build();
        }
        catch (ArgumentException ex) when (ex.Message.Contains("Docker", StringComparison.OrdinalIgnoreCase))
        {
            Assert.Skip("Docker is not available on this host — MinIO integration test requires Docker (runs in CI).");
            return;
        }

        await _container.StartAsync(TestContext.Current.CancellationToken);
        Endpoint = _container.GetConnectionString();

        using var s3 = CreateClient();
        try
        {
            await s3.PutBucketAsync(
                new PutBucketRequest { BucketName = BucketName },
                TestContext.Current.CancellationToken);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.Conflict)
        {
            // Bucket already exists — benign.
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    /// <summary>Builds a new <see cref="IAmazonS3"/> client aimed at the running MinIO container.</summary>
    public IAmazonS3 CreateClient() =>
        new AmazonS3Client(
            new BasicAWSCredentials(AccessKey, SecretKey),
            new AmazonS3Config
            {
                ServiceURL = Endpoint,
                ForcePathStyle = true,
            });
}
