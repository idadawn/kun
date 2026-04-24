using System.Net.Http;
using System.Text;
using FluentAssertions;
using Kun.IntegrationTests.Fixtures;
using Kun.Storage.Abstractions;
using Kun.Storage.Implementations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Kun.IntegrationTests.Storage;

/// <summary>
/// Round-trips <see cref="S3FileStorage"/> against a live MinIO container.
/// </summary>
public sealed class S3FileStorageIntegrationTests : IClassFixture<MinioFixture>
{
    private readonly MinioFixture _fixture;
    private readonly S3FileStorage _storage;
    private readonly CancellationToken _ct;

    public S3FileStorageIntegrationTests(MinioFixture fixture)
    {
        _fixture = fixture;
        _storage = new S3FileStorage(
            fixture.CreateClient(),
            Options.Create(new StorageOptions { BucketName = fixture.BucketName }),
            Substitute.For<ILogger<S3FileStorage>>());
        _ct = TestContext.Current.CancellationToken;
    }

    [Fact]
    public async Task Put_Head_Get_Delete_RoundTrips_A_Binary_Object()
    {
        var key = $"rt/{Guid.NewGuid():N}.bin";
        var payload = Encoding.UTF8.GetBytes("hello-minio");

        using (var upload = new MemoryStream(payload))
        {
            await _storage.PutObjectAsync(
                key, upload,
                new PutObjectOptions(ContentType: "application/octet-stream"),
                _ct);
        }

        var head = await _storage.HeadObjectAsync(key, _ct);
        head.Should().NotBeNull();
        head!.ContentLength.Should().Be(payload.Length);
        head.ContentType.Should().Be("application/octet-stream");

        await using (var download = await _storage.GetObjectAsync(key, _ct))
        {
            using var ms = new MemoryStream();
            await download.Content.CopyToAsync(ms, _ct);
            ms.ToArray().Should().Equal(payload);
        }

        await _storage.DeleteObjectAsync(key, _ct);
        (await _storage.HeadObjectAsync(key, _ct)).Should().BeNull();
    }

    [Fact]
    public async Task Put_Persists_Attachment_Disposition_For_Unsafe_Mime()
    {
        var key = $"disp/{Guid.NewGuid():N}.html";
        using var upload = new MemoryStream(Encoding.UTF8.GetBytes("<script>alert(1)</script>"));

        await _storage.PutObjectAsync(
            key, upload,
            new PutObjectOptions(ContentType: "text/html", FileName: "evil.html"),
            _ct);

        var head = await _storage.HeadObjectAsync(key, _ct);
        head.Should().NotBeNull();
        head!.ContentDisposition.Should().NotBeNull();
        head.ContentDisposition!.Should().StartWith("attachment");
    }

    [Fact]
    public async Task List_Yields_Uploaded_Objects_Matching_Prefix()
    {
        var prefix = $"list/{Guid.NewGuid():N}/";
        await PutTiny(prefix + "a.txt");
        await PutTiny(prefix + "b.txt");
        await PutTiny($"other/{Guid.NewGuid():N}.txt");

        var keys = new List<string>();
        await foreach (var summary in _storage.ListObjectsAsync(prefix, _ct))
        {
            keys.Add(summary.Path);
        }

        keys.Should().BeEquivalentTo(new[] { prefix + "a.txt", prefix + "b.txt" });
    }

    [Fact]
    public async Task Presigned_Upload_Url_Accepts_Client_PUT_Through_HttpClient()
    {
        var key = $"presign-up/{Guid.NewGuid():N}.txt";
        var url = await _storage.CreatePresignedUploadUrlAsync(
            key,
            new PresignedUploadRequest(TimeSpan.FromMinutes(5), ContentType: "text/plain"),
            _ct);

        using var http = new HttpClient();
        using var put = new HttpRequestMessage(HttpMethod.Put, url.Url)
        {
            Content = new StringContent("client-uploaded", Encoding.UTF8, "text/plain"),
        };
        foreach (var (k, v) in url.RequiredHeaders)
        {
            put.Content.Headers.Remove(k);
            put.Content.Headers.TryAddWithoutValidation(k, v);
        }

        using var response = await http.SendAsync(put, _ct);
        response.IsSuccessStatusCode.Should().BeTrue(
            "presigned PUT must succeed — status was {0} {1}", (int)response.StatusCode, response.ReasonPhrase);

        (await _storage.HeadObjectAsync(key, _ct)).Should().NotBeNull();
    }

    [Fact]
    public async Task Presigned_Download_Url_Honours_Attachment_Override_For_Unsafe_Mime()
    {
        var key = $"presign-down/{Guid.NewGuid():N}.svg";
        using (var upload = new MemoryStream(Encoding.UTF8.GetBytes("<svg/>")))
        {
            await _storage.PutObjectAsync(
                key, upload,
                new PutObjectOptions(ContentType: "image/svg+xml"),
                _ct);
        }

        var url = await _storage.CreatePresignedDownloadUrlAsync(
            key,
            new PresignedDownloadRequest(TimeSpan.FromMinutes(5)),
            _ct);

        using var http = new HttpClient();
        using var response = await http.GetAsync(url.Url, _ct);
        response.IsSuccessStatusCode.Should().BeTrue();

        var disposition = response.Content.Headers.ContentDisposition?.ToString();
        disposition.Should().NotBeNull();
        disposition!.Should().StartWith("attachment");
    }

    private async Task PutTiny(string key)
    {
        using var ms = new MemoryStream(new byte[] { 0xAA });
        await _storage.PutObjectAsync(
            key, ms,
            new PutObjectOptions(ContentType: "application/octet-stream"),
            _ct);
    }
}
