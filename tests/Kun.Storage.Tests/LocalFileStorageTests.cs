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
    public async Task PutAndGet_Works()
    {
        var content = Encoding.UTF8.GetBytes("hello world");
        using var stream = new MemoryStream(content);

        await _storage.PutAsync("test/file.txt", stream, "text/plain", _ct);
        using var result = await _storage.GetAsync("test/file.txt", _ct);
        using var reader = new StreamReader(result);
        var text = await reader.ReadToEndAsync(_ct);

        text.Should().Be("hello world");
    }

    [Fact]
    public async Task Exists_ReturnsTrue_AfterPut()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("data"));
        await _storage.PutAsync("exists-test.txt", stream, "text/plain", _ct);

        var exists = await _storage.ExistsAsync("exists-test.txt", _ct);

        exists.Should().BeTrue();
    }

    [Fact]
    public async Task Delete_RemovesFile()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("to-delete"));
        await _storage.PutAsync("delete-test.txt", stream, "text/plain", _ct);

        await _storage.DeleteAsync("delete-test.txt", _ct);
        var exists = await _storage.ExistsAsync("delete-test.txt", _ct);

        exists.Should().BeFalse();
    }

    [Fact]
    public async Task Exists_ReturnsFalse_ForMissingFile()
    {
        var exists = await _storage.ExistsAsync("does-not-exist.txt", _ct);
        exists.Should().BeFalse();
    }

    [Fact]
    public async Task Delete_DoesNotThrow_ForMissingFile()
    {
        var act = () => _storage.DeleteAsync("nonexistent.txt", _ct);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task GetPresignedUrl_Throws_NotSupportedException()
    {
        var act = () => _storage.GetPresignedUrlAsync("file.txt", TimeSpan.FromMinutes(5), _ct);
        await act.Should().ThrowAsync<NotSupportedException>();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }
}
