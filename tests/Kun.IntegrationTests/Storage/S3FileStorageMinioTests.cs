using System.Text;
using Amazon.S3;
using FluentAssertions;
using Kun.Storage.S3;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Testcontainers.Minio;
using Xunit;

namespace Kun.IntegrationTests.Storage;

/// <summary>
/// End-to-end integration test for <see cref="S3FileStorage"/> against a MinIO container.
/// Covers the M1 invariant: all four canonical active web content types must round-trip
/// and their presigned download URL must carry <c>response-content-disposition=attachment</c>.
/// </summary>
public sealed class S3FileStorageMinioTests : IAsyncLifetime
{
    private MinioContainer? _container;
    private AmazonS3Client? _s3;
    private S3FileStorage? _storage;
    private const string BucketName = "kun-storage-tests";

    public async ValueTask InitializeAsync()
    {
        try
        {
            _container = new MinioBuilder()
                .WithImage("minio/minio:RELEASE.2024-06-13T22-53-53Z")
                .WithUsername("kunadmin")
                .WithPassword("kunadmin-secret")
                .Build();
        }
        catch (ArgumentException ex) when (ex.Message.Contains("Docker", StringComparison.OrdinalIgnoreCase))
        {
            Assert.Skip("Docker is not available on this host — integration test requires Docker.");
            return;
        }

        try
        {
            await _container.StartAsync(TestContext.Current.CancellationToken);
        }
        catch (Exception ex) when (ex.GetType().Name.Contains("Docker", StringComparison.OrdinalIgnoreCase))
        {
            Assert.Skip($"Docker is not available on this host: {ex.Message}");
            return;
        }

        var options = new S3StorageOptions
        {
            BucketName = BucketName,
            ServiceUrl = new Uri(_container.GetConnectionString(), UriKind.Absolute),
            ForcePathStyle = true,
            AccessKey = "kunadmin",
            SecretKey = "kunadmin-secret",
            Region = "us-east-1",
        };

        var cfg = new AmazonS3Config
        {
            ServiceURL = options.ServiceUrl.AbsoluteUri,
            ForcePathStyle = true,
            AuthenticationRegion = options.Region,
        };

        _s3 = new AmazonS3Client(
            new Amazon.Runtime.BasicAWSCredentials(options.AccessKey, options.SecretKey),
            cfg);

        await _s3.PutBucketAsync(
            new Amazon.S3.Model.PutBucketRequest { BucketName = BucketName },
            TestContext.Current.CancellationToken);

        _storage = new S3FileStorage(
            _s3,
            Options.Create(options),
            NullLogger<S3FileStorage>.Instance,
            TimeProvider.System);
    }

    public async ValueTask DisposeAsync()
    {
        _s3?.Dispose();
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    [Theory]
    [InlineData("text/html", "<html><body>hi</body></html>")]
    [InlineData("image/svg+xml", "<svg xmlns='http://www.w3.org/2000/svg'/>")]
    [InlineData("application/xhtml+xml", "<html xmlns='http://www.w3.org/1999/xhtml'></html>")]
    [InlineData("application/xml", "<root/>")]
    public async Task RoundTrip_ActiveWebContentType_ForcesAttachmentDisposition(string contentType, string payload)
    {
        if (_storage is null)
        {
            Assert.Skip("Container was not started (Docker unavailable).");
            return;
        }

        var key = $"active/{Guid.NewGuid():N}.blob";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(payload));

        var meta = await _storage.UploadAsync(
            key, stream, contentType,
            cancellationToken: TestContext.Current.CancellationToken);
        meta.Size.Should().Be(payload.Length);

        var presigned = await _storage.GetPresignedDownloadAsync(
            key, cancellationToken: TestContext.Current.CancellationToken);

        presigned.ContentDispositionForced.Should().BeTrue();
        presigned.ResponseHeaders.Should().ContainKey("Content-Disposition");

        // Presigned URL itself carries response-content-disposition=… as a signed query param
        // (AWS SDK maps ResponseHeaderOverrides.ContentDisposition to this parameter).
        presigned.Url.Query.Should().Contain("response-content-disposition", because:
            "S3 presigned URLs encode the Content-Disposition override as a query parameter.");

        (await _storage.DeleteAsync(key, TestContext.Current.CancellationToken)).Should().BeTrue();
    }
}
