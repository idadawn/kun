using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Amazon.S3;
using FluentAssertions;
using Kun.Storage;
using Kun.Storage.S3;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Testcontainers.Minio;
using Xunit;

namespace Kun.IntegrationTests.Storage;

/// <summary>
/// End-to-end integration test for <see cref="S3FileStorage.GetPresignedPostUploadAsync"/>
/// against MinIO. The acceptance criterion under test: an oversized multipart POST is
/// rejected by S3 itself (HTTP 400 / EntityTooLarge) because the <c>content-length-range</c>
/// condition is baked into the signed policy — the framework does NOT need to pre-validate
/// the body size.
/// </summary>
public sealed class S3FileStoragePresignedPostMinioTests : IAsyncLifetime
{
    private const string BucketName = "kun-storage-post-tests";
    private const string AccessKey = "kunadmin";
    private const string SecretKey = "kunadmin-secret";

    private MinioContainer? _container;
    private AmazonS3Client? _s3;
    private S3FileStorage? _storage;

    public async ValueTask InitializeAsync()
    {
        try
        {
            _container = new MinioBuilder()
                .WithImage("minio/minio:RELEASE.2024-06-13T22-53-53Z")
                .WithUsername(AccessKey)
                .WithPassword(SecretKey)
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
            AccessKey = AccessKey,
            SecretKey = SecretKey,
            Region = "us-east-1",
        };

        var cfg = new AmazonS3Config
        {
            ServiceURL = options.ServiceUrl.AbsoluteUri,
            ForcePathStyle = true,
            AuthenticationRegion = options.Region,
        };

        _s3 = new AmazonS3Client(new Amazon.Runtime.BasicAWSCredentials(AccessKey, SecretKey), cfg);

        await _s3.PutBucketAsync(
            new Amazon.S3.Model.PutBucketRequest { BucketName = BucketName },
            TestContext.Current.CancellationToken);

        _storage = new S3FileStorage(
            _s3, Options.Create(options), NullLogger<S3FileStorage>.Instance, TimeProvider.System);
    }

    public async ValueTask DisposeAsync()
    {
        _s3?.Dispose();
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    [Fact]
    public async Task PresignedPost_Accepts_UnderLimit_Upload()
    {
        if (_storage is null)
        {
            Assert.Skip("Container was not started (Docker unavailable).");
            return;
        }

        var key = $"post/{Guid.NewGuid():N}.bin";
        var payload = Encoding.UTF8.GetBytes("small payload");
        var presigned = await _storage.GetPresignedPostUploadAsync(
            key,
            constraints: new UploadConstraints(MaxBytes: 1024),
            expiresIn: TimeSpan.FromMinutes(5),
            cancellationToken: TestContext.Current.CancellationToken);

        using var http = new HttpClient();
        using var content = BuildMultipart(presigned.FormFields, payload, "application/octet-stream");
        using var response = await http.PostAsync(presigned.Url, content, TestContext.Current.CancellationToken);

        response.IsSuccessStatusCode.Should().BeTrue(
            $"MinIO must accept an under-limit POST upload; got {(int)response.StatusCode} — " +
            $"body: {await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)}");

        (await _storage.ExistsAsync(key, TestContext.Current.CancellationToken)).Should().BeTrue();
        (await _storage.DeleteAsync(key, TestContext.Current.CancellationToken)).Should().BeTrue();
    }

    [Fact]
    public async Task PresignedPost_Rejects_OverLimit_Upload_WithHttp400()
    {
        if (_storage is null)
        {
            Assert.Skip("Container was not started (Docker unavailable).");
            return;
        }

        var key = $"post/{Guid.NewGuid():N}.bin";
        var oversize = new byte[2048]; // exceeds MaxBytes=1024
        for (var i = 0; i < oversize.Length; i++)
        {
            oversize[i] = 0x41;
        }

        var presigned = await _storage.GetPresignedPostUploadAsync(
            key,
            constraints: new UploadConstraints(MaxBytes: 1024),
            expiresIn: TimeSpan.FromMinutes(5),
            cancellationToken: TestContext.Current.CancellationToken);

        using var http = new HttpClient();
        using var content = BuildMultipart(presigned.FormFields, oversize, "application/octet-stream");
        using var response = await http.PostAsync(presigned.Url, content, TestContext.Current.CancellationToken);

        // Per AWS spec, an oversized body produces 400 Bad Request (EntityTooLarge);
        // MinIO mirrors that. The framework itself never saw the body — S3 rejected it.
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "the signed policy's content-length-range must cause S3 to reject oversized uploads at the edge");

        (await _storage.ExistsAsync(key, TestContext.Current.CancellationToken)).Should().BeFalse(
            "no bytes must have landed in the bucket when S3 rejects the POST");
    }

    private static MultipartFormDataContent BuildMultipart(
        IReadOnlyDictionary<string, string> formFields,
        byte[] file,
        string fileContentType)
    {
        var mp = new MultipartFormDataContent();
        foreach (var (k, v) in formFields)
        {
            // StringContent serializes with a Content-Type header by default; strip it so the
            // signed policy isn't confused by extra part headers, and set a plain name-only
            // Content-Disposition matching the AWS POST form spec.
            var part = new StringContent(v);
            part.Headers.ContentType = null;
            part.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data")
            {
                Name = $"\"{k}\"",
            };
            mp.Add(part);
        }

        var fileContent = new ByteArrayContent(file);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(fileContentType);
        mp.Add(fileContent, "file", "upload.bin");
        return mp;
    }
}
