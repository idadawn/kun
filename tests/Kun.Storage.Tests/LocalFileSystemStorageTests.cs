using System.Text;
using FluentAssertions;
using Kun.Storage.Configuration;
using Kun.Storage.Implementations;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Kun.Storage.Tests;

public sealed class LocalFileSystemStorageTests : IDisposable
{
    private readonly string _tempDir;
    private readonly LocalFileSystemStorage _storage;
    private readonly CancellationToken _ct;

    public LocalFileSystemStorageTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"kun-storage-test-{Guid.NewGuid():N}");
        var options = Options.Create(new LocalStorageOptions { RootPath = _tempDir });
        _storage = new LocalFileSystemStorage(options, NullLogger<LocalFileSystemStorage>.Instance);
        _ct = TestContext.Current.CancellationToken;
    }

    [Fact]
    public async Task PutObject_StoresBodyAndReturnsMetadata()
    {
        var payload = Encoding.UTF8.GetBytes("hello world");
        using var body = new MemoryStream(payload);

        var metadata = await _storage.PutObjectAsync("tenant-a/file.txt", body, "text/plain", _ct);

        metadata.ObjectKey.Should().Be("tenant-a/file.txt");
        metadata.Size.Should().Be(payload.Length);
        metadata.ContentType.Should().Be("text/plain");
        metadata.Sha256.Should().HaveLength(64);
    }

    [Fact]
    public async Task GetObject_ReturnsStoredBody()
    {
        using (var body = new MemoryStream(Encoding.UTF8.GetBytes("roundtrip")))
        {
            await _storage.PutObjectAsync("round.txt", body, "text/plain", _ct);
        }

        await using var stream = await _storage.GetObjectAsync("round.txt", _ct);
        using var reader = new StreamReader(stream);
        var text = await reader.ReadToEndAsync(_ct);

        text.Should().Be("roundtrip");
    }

    [Fact]
    public async Task HeadObject_RecallsContentTypeAndHash()
    {
        var payload = Encoding.UTF8.GetBytes("hash me");
        using (var body = new MemoryStream(payload))
        {
            await _storage.PutObjectAsync("hash.txt", body, "text/plain", _ct);
        }

        var head = await _storage.HeadObjectAsync("hash.txt", _ct);

        head.Should().NotBeNull();
        head!.ObjectKey.Should().Be("hash.txt");
        head.Size.Should().Be(payload.Length);
        head.ContentType.Should().Be("text/plain");
        head.Sha256.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task HeadObject_ReturnsNull_WhenMissing()
    {
        var head = await _storage.HeadObjectAsync("nope.txt", _ct);
        head.Should().BeNull();
    }

    [Fact]
    public async Task DeleteObject_RemovesBodyAndMetadata()
    {
        using (var body = new MemoryStream(Encoding.UTF8.GetBytes("gone")))
        {
            await _storage.PutObjectAsync("delete.txt", body, "text/plain", _ct);
        }

        await _storage.DeleteObjectAsync("delete.txt", _ct);

        (await _storage.HeadObjectAsync("delete.txt", _ct)).Should().BeNull();
    }

    [Fact]
    public async Task DeleteObject_IsIdempotentForMissingKey()
    {
        var act = async () => await _storage.DeleteObjectAsync("never-existed.txt", _ct);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task GetObject_ThrowsFileNotFound_WhenMissing()
    {
        var act = async () => await _storage.GetObjectAsync("missing.txt", _ct);
        await act.Should().ThrowAsync<FileNotFoundException>();
    }

    [Fact]
    public async Task ListObjects_RespectsPrefix()
    {
        using (var b1 = new MemoryStream([0x31]))
        {
            await _storage.PutObjectAsync("tenant-a/one.txt", b1, "text/plain", _ct);
        }
        using (var b2 = new MemoryStream([0x32]))
        {
            await _storage.PutObjectAsync("tenant-a/two.txt", b2, "text/plain", _ct);
        }
        using (var b3 = new MemoryStream([0x33]))
        {
            await _storage.PutObjectAsync("tenant-b/three.txt", b3, "text/plain", _ct);
        }

        var tenantA = new List<string>();
        await foreach (var entry in _storage.ListObjectsAsync("tenant-a/", _ct))
        {
            tenantA.Add(entry.ObjectKey);
        }

        tenantA.Should().BeEquivalentTo(["tenant-a/one.txt", "tenant-a/two.txt"]);
    }

    [Fact]
    public async Task PresignedUrl_NotSupported()
    {
        await ((Func<Task>)(async () =>
            await _storage.CreatePresignedUploadUrlAsync("x", TimeSpan.FromMinutes(1), ct: _ct)))
            .Should().ThrowAsync<NotSupportedException>();

        await ((Func<Task>)(async () =>
            await _storage.CreatePresignedDownloadUrlAsync("x", TimeSpan.FromMinutes(1), _ct)))
            .Should().ThrowAsync<NotSupportedException>();
    }

    [Theory]
    [InlineData("../escape.txt")]
    [InlineData("/absolute.txt")]
    public async Task ObjectKey_EscapingRoot_IsRejected(string objectKey)
    {
        using var body = new MemoryStream([0]);
        var act = async () => await _storage.PutObjectAsync(objectKey, body, "text/plain", _ct);
        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }
}
