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

        results.Select(s => s.Path).Should().BeEquivalentTo(
            new[] { "users/alice/profile.json", "users/bob/profile.json" });
    }

    [Fact]
    public async Task CreatePresignedUploadUrl_Throws_NotSupported_On_Local()
    {
        var act = () => _storage.CreatePresignedUploadUrlAsync(
            "x.txt",
            new PresignedUploadRequest(TimeSpan.FromMinutes(5)),
            _ct);
        await act.Should().ThrowAsync<NotSupportedException>();
    }

    [Fact]
    public async Task CreatePresignedDownloadUrl_Throws_NotSupported_On_Local()
    {
        var act = () => _storage.CreatePresignedDownloadUrlAsync(
            "x.txt",
            new PresignedDownloadRequest(TimeSpan.FromMinutes(5)),
            _ct);
        await act.Should().ThrowAsync<NotSupportedException>();
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
