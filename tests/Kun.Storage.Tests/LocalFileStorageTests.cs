using System.Text;
using FluentAssertions;
using Kun.Storage.Abstractions;
using Kun.Storage.Implementations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Kun.Storage.Tests;

public sealed class LocalFileStorageTests : IDisposable
{
    private static readonly string[] ExpectedUserPaths = ["users/alice/profile.json", "users/bob/profile.json"];

    private readonly string _tempDir;
    private readonly LocalFileStorage _storage;
    private readonly CancellationToken _ct;

    public LocalFileStorageTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"kun-storage-test-{Guid.NewGuid():N}");
        var options = Options.Create(new StorageOptions { RootPath = _tempDir });
        var logger = Substitute.For<ILogger<LocalFileStorage>>();
        _storage = new LocalFileStorage(options, logger);
        _ct = TestContext.Current.CancellationToken;
    }

    [Fact]
    public async Task PutObject_Then_GetObject_Roundtrips_Content_And_Metadata()
    {
        var bytes = Encoding.UTF8.GetBytes("hello world");
        using var stream = new MemoryStream(bytes);

        await _storage.PutObjectAsync(
            "test/file.txt",
            stream,
            new PutObjectOptions(ContentType: "text/plain", FileName: "file.txt"),
            _ct);

        await using var download = await _storage.GetObjectAsync("test/file.txt", _ct);
        using var reader = new StreamReader(download.Content);
        var text = await reader.ReadToEndAsync(_ct);

        text.Should().Be("hello world");
        download.Metadata.ContentType.Should().Be("text/plain");
        download.Metadata.ContentLength.Should().Be(bytes.Length);
    }

    [Fact]
    public async Task HeadObject_Returns_Metadata_After_Put()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("data"));
        await _storage.PutObjectAsync(
            "exists-test.txt",
            stream,
            new PutObjectOptions(ContentType: "application/json"),
            _ct);

        var metadata = await _storage.HeadObjectAsync("exists-test.txt", _ct);

        metadata.Should().NotBeNull();
        metadata!.ContentType.Should().Be("application/json");
        metadata.ContentLength.Should().Be(4);
    }

    [Fact]
    public async Task HeadObject_Returns_Null_For_Missing_File()
    {
        var metadata = await _storage.HeadObjectAsync("does-not-exist.txt", _ct);
        metadata.Should().BeNull();
    }

    [Fact]
    public async Task DeleteObject_Removes_File_And_Sidecar()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("to-delete"));
        await _storage.PutObjectAsync(
            "delete-test.txt",
            stream,
            new PutObjectOptions(ContentType: "text/plain"),
            _ct);

        await _storage.DeleteObjectAsync("delete-test.txt", _ct);
        var metadata = await _storage.HeadObjectAsync("delete-test.txt", _ct);

        metadata.Should().BeNull();
    }

    [Fact]
    public async Task DeleteObject_Is_NoOp_For_Missing_File()
    {
        var act = () => _storage.DeleteObjectAsync("nonexistent.txt", _ct);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task GetObject_Throws_FileNotFoundException_For_Missing_Path()
    {
        var act = () => _storage.GetObjectAsync("missing.txt", _ct);
        await act.Should().ThrowAsync<FileNotFoundException>();
    }

    [Fact]
    public async Task ListObjects_Yields_Uploaded_Files_With_Prefix()
    {
        await Put("users/alice/profile.json", "{}", "application/json");
        await Put("users/bob/profile.json", "{}", "application/json");
        await Put("reports/q1.txt", "hello", "text/plain");

        var results = new List<ObjectSummary>();
        await foreach (var summary in _storage.ListObjectsAsync("users/", _ct))
        {
            results.Add(summary);
        }

        results.Select(s => s.Path).Should().BeEquivalentTo(ExpectedUserPaths);
    }

    [Fact]
    public async Task CreatePresignedUploadUrl_Returns_SignedStubUrl()
    {
        var result = await _storage.CreatePresignedUploadUrlAsync(
            "uploads/file.bin",
            new PresignedUploadRequest(TimeSpan.FromMinutes(5), ContentType: "application/octet-stream"),
            _ct);

        result.HttpMethod.Should().Be("PUT");
        result.Url.Scheme.Should().Be("kun-local");
        result.Url.Query.Should().Contain("op=upload")
            .And.Contain("expires=")
            .And.Contain("sig=");
        result.RequiredHeaders.Should().ContainKey("Content-Type")
            .WhoseValue.Should().Be("application/octet-stream");
        result.ExpiresAt.Should().BeCloseTo(DateTimeOffset.UtcNow.AddMinutes(5), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task CreatePresignedDownloadUrl_Returns_SignedStubUrl()
    {
        var result = await _storage.CreatePresignedDownloadUrlAsync(
            "downloads/file.bin",
            new PresignedDownloadRequest(TimeSpan.FromMinutes(5)),
            _ct);

        result.Url.Scheme.Should().Be("kun-local");
        result.Url.Query.Should().Contain("op=download")
            .And.Contain("sig=");
    }

    [Theory]
    [InlineData("../secret.txt")]
    [InlineData("foo/../../etc/passwd")]
    [InlineData("/abs/path.txt")]
    public async Task PutObject_Rejects_Paths_That_Escape_Root(string escapingPath)
    {
        using var stream = new MemoryStream(new byte[] { 1, 2, 3 });
        var act = () => _storage.PutObjectAsync(
            escapingPath, stream, new PutObjectOptions("application/octet-stream"), _ct);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task PutObject_Forces_Attachment_Disposition_For_Unsafe_MimeType()
    {
        using var html = new MemoryStream(Encoding.UTF8.GetBytes("<script>alert(1)</script>"));
        await _storage.PutObjectAsync(
            "user-upload/evil.html",
            html,
            new PutObjectOptions(ContentType: "text/html", FileName: "evil.html"),
            _ct);

        var metadata = await _storage.HeadObjectAsync("user-upload/evil.html", _ct);

        metadata.Should().NotBeNull();
        metadata!.ContentDisposition.Should().NotBeNull();
        metadata.ContentDisposition!.Should().StartWith("attachment");
    }

    [Fact]
    public async Task CreatePresignedDownloadUrl_Forces_Attachment_For_Unsafe_MimeType()
    {
        using var svg = new MemoryStream(Encoding.UTF8.GetBytes("<svg/>"));
        await _storage.PutObjectAsync(
            "inline-risk.svg",
            svg,
            new PutObjectOptions(ContentType: "image/svg+xml"),
            _ct);

        var result = await _storage.CreatePresignedDownloadUrlAsync(
            "inline-risk.svg",
            new PresignedDownloadRequest(TimeSpan.FromMinutes(5)),
            _ct);

        Uri.UnescapeDataString(result.Url.Query).Should().Contain("cd=attachment");
    }

    private async Task Put(string path, string content, string contentType)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        await _storage.PutObjectAsync(path, stream, new PutObjectOptions(ContentType: contentType), _ct);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }
}
