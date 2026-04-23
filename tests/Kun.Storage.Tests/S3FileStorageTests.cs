using System.Text;
using DotNet.Testcontainers.Builders;
using FluentAssertions;
using Kun.Storage.Configuration;
using Kun.Storage.Implementations;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Testcontainers.Minio;
using Xunit;

namespace Kun.Storage.Tests;

/// <summary>
/// Integration tests against a live MinIO container. Skipped locally
/// without Docker; the CI ubuntu-latest runner has Docker available so
/// Testcontainers will spin MinIO on demand.
/// </summary>
public sealed class S3FileStorageTests : IAsyncLifetime
{
    private const string Bucket = "kun-storage-test";
    private readonly MinioContainer _minio = new MinioBuilder()
        .WithImage("minio/minio:RELEASE.2024-10-13T13-34-11Z")
        .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("API:"))
        .Build();

    private S3FileStorage? _storage;
    private Amazon.S3.AmazonS3Client? _s3;

    public async ValueTask InitializeAsync()
    {
        await _minio.StartAsync();

        _s3 = new Amazon.S3.AmazonS3Client(
            new Amazon.Runtime.BasicAWSCredentials(_minio.GetAccessKey(), _minio.GetSecretKey()),
            new Amazon.S3.AmazonS3Config
            {
                ServiceURL = _minio.GetConnectionString(),
                ForcePathStyle = true,
                UseHttp = true,
            });

        await _s3.PutBucketAsync(new Amazon.S3.Model.PutBucketRequest { BucketName = Bucket });

        var options = Options.Create(new S3StorageOptions
        {
            BucketName = Bucket,
            Endpoint = _minio.GetConnectionString(),
            AccessKey = _minio.GetAccessKey(),
            SecretKey = _minio.GetSecretKey(),
            ForcePathStyle = true,
        });
        _storage = new S3FileStorage(_s3, options, NullLogger<S3FileStorage>.Instance);
    }

    public async ValueTask DisposeAsync()
    {
        _s3?.Dispose();
        await _minio.DisposeAsync();
    }

    [Fact]
    public async Task PutAndHead_RoundTrip()
    {
        var ct = TestContext.Current.CancellationToken;
        using var body = new MemoryStream(Encoding.UTF8.GetBytes("hello minio"));

        var put = await _storage!.PutObjectAsync("folder/obj.txt", body, "text/plain", ct);
        var head = await _storage.HeadObjectAsync("folder/obj.txt", ct);

        put.ObjectKey.Should().Be("folder/obj.txt");
        head.Should().NotBeNull();
        head!.Size.Should().Be("hello minio".Length);
        head.ContentType.Should().Be("text/plain");
    }

    [Fact]
    public async Task ActiveWebType_ForcesContentDispositionAttachment()
    {
        var ct = TestContext.Current.CancellationToken;
        using var body = new MemoryStream(Encoding.UTF8.GetBytes("<html><script>alert(1)</script></html>"));

        await _storage!.PutObjectAsync("evil.html", body, "text/html", ct);

        var response = await _s3!.GetObjectAsync(new Amazon.S3.Model.GetObjectRequest
        {
            BucketName = Bucket,
            Key = "evil.html",
        }, ct);

        response.Headers.ContentDisposition.Should().StartWith("attachment;");
        response.Headers.ContentDisposition.Should().Contain("evil.html");
    }

    [Fact]
    public async Task ListObjects_Paginates()
    {
        var ct = TestContext.Current.CancellationToken;
        for (var i = 0; i < 5; i++)
        {
            using var body = new MemoryStream(Encoding.UTF8.GetBytes($"item-{i}"));
            await _storage!.PutObjectAsync($"list/{i}.txt", body, "text/plain", ct);
        }

        var keys = new List<string>();
        await foreach (var entry in _storage!.ListObjectsAsync("list/", ct))
        {
            keys.Add(entry.ObjectKey);
        }

        keys.Should().HaveCount(5).And.OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task HeadObject_ReturnsNull_ForMissingKey()
    {
        var ct = TestContext.Current.CancellationToken;
        var head = await _storage!.HeadObjectAsync("does/not/exist.bin", ct);
        head.Should().BeNull();
    }

    [Fact]
    public async Task PresignedDownloadUrl_IsUsableByAnonymousClient()
    {
        var ct = TestContext.Current.CancellationToken;
        var payload = "presigned-download"u8.ToArray();
        using (var body = new MemoryStream(payload))
        {
            await _storage!.PutObjectAsync("pre/download.bin", body, "application/octet-stream", ct);
        }

        var presigned = await _storage!.CreatePresignedDownloadUrlAsync(
            "pre/download.bin",
            TimeSpan.FromMinutes(2),
            ct);

        using var http = new HttpClient();
        var fetched = await http.GetByteArrayAsync(presigned.Url, ct);
        fetched.Should().Equal(payload);
    }
}
