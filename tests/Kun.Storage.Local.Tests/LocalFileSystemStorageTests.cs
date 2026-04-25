using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Kun.Storage.Local.Tests;

/// <summary>
/// Round-trip tests for <see cref="LocalFileSystemStorage"/> against a throw-away temp
/// directory. Covers upload → metadata → download URL shape → force-download
/// activation → delete.
/// </summary>
public sealed class LocalFileSystemStorageTests : IDisposable
{
    private static readonly string[] ExpectedPrefixAKeys = ["a/1.bin", "a/2.bin"];

    private readonly string _root;
    private readonly LocalFileSystemStorage _storage;

    public LocalFileSystemStorageTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "kun-storage-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);

        var options = Options.Create(new LocalFileStorageOptions
        {
            RootPath = _root,
            PublicBaseUrl = new Uri("http://localhost:5000/storage/", UriKind.Absolute),
            DefaultPresignedUrlLifetime = TimeSpan.FromMinutes(5),
        });

        _storage = new LocalFileSystemStorage(
            options, NullLogger<LocalFileSystemStorage>.Instance, TimeProvider.System);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { /* best effort */ }
    }

    [Fact]
    public async Task UploadAsync_WritesFile_AndReturnsMetadata()
    {
        var payload = Encoding.UTF8.GetBytes("hello kun");
        using var stream = new MemoryStream(payload);

        var meta = await _storage.UploadAsync(
            "docs/readme.txt", stream, "text/plain",
            cancellationToken: TestContext.Current.CancellationToken);

        meta.Key.Should().Be("docs/readme.txt");
        meta.Size.Should().Be(payload.Length);
        meta.ContentType.Should().Be("text/plain");
        File.Exists(Path.Combine(_root, "docs", "readme.txt")).Should().BeTrue();
    }

    [Fact]
    public async Task ExistsAsync_ReflectsUpload()
    {
        using var stream = new MemoryStream([1, 2, 3]);
        await _storage.UploadAsync("a/b.bin", stream, "application/octet-stream",
            cancellationToken: TestContext.Current.CancellationToken);

        (await _storage.ExistsAsync("a/b.bin", TestContext.Current.CancellationToken)).Should().BeTrue();
        (await _storage.ExistsAsync("missing.bin", TestContext.Current.CancellationToken)).Should().BeFalse();
    }

    [Fact]
    public async Task GetMetadataAsync_ReturnsNullForMissing()
    {
        (await _storage.GetMetadataAsync("nope/here.bin", TestContext.Current.CancellationToken))
            .Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_RemovesFile_AndSidecar()
    {
        using var stream = new MemoryStream([1]);
        await _storage.UploadAsync("x.bin", stream, "application/octet-stream",
            cancellationToken: TestContext.Current.CancellationToken);

        (await _storage.DeleteAsync("x.bin", TestContext.Current.CancellationToken)).Should().BeTrue();
        (await _storage.ExistsAsync("x.bin", TestContext.Current.CancellationToken)).Should().BeFalse();
        (await _storage.DeleteAsync("x.bin", TestContext.Current.CancellationToken)).Should().BeFalse();
    }

    [Theory]
    [InlineData("text/html")]
    [InlineData("image/svg+xml")]
    [InlineData("application/xhtml+xml")]
    [InlineData("application/xml")]
    public async Task GetPresignedDownloadAsync_ForcesAttachment_OnActiveWebContentType(string contentType)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("<html></html>"));
        await _storage.UploadAsync("pages/evil.bin", stream, contentType,
            cancellationToken: TestContext.Current.CancellationToken);

        var presigned = await _storage.GetPresignedDownloadAsync(
            "pages/evil.bin", cancellationToken: TestContext.Current.CancellationToken);

        presigned.ContentDispositionForced.Should().BeTrue();
        presigned.ResponseHeaders.Should().ContainKey("Content-Disposition");
        presigned.ResponseHeaders["Content-Disposition"].Should().StartWith("attachment;");
    }

    [Fact]
    public async Task GetPresignedDownloadAsync_DoesNotForce_OnBenignContentType()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("png"));
        await _storage.UploadAsync("pic.png", stream, "image/png",
            cancellationToken: TestContext.Current.CancellationToken);

        var presigned = await _storage.GetPresignedDownloadAsync(
            "pic.png", cancellationToken: TestContext.Current.CancellationToken);

        presigned.ContentDispositionForced.Should().BeFalse();
    }

    [Fact]
    public async Task GetPresignedUploadAsync_ReturnsUrlAndHeaders()
    {
        var presigned = await _storage.GetPresignedUploadAsync(
            "uploads/new.bin", "application/octet-stream",
            cancellationToken: TestContext.Current.CancellationToken);

        presigned.Url.IsAbsoluteUri.Should().BeTrue();
        presigned.RequiredHeaders.Should().ContainKey("Content-Type");
        presigned.RequiredHeaders["Content-Type"].Should().Be("application/octet-stream");
        presigned.ExpiresAt.Should().BeAfter(DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task ListAsync_ReturnsObjects_ByPrefix()
    {
        using (var s1 = new MemoryStream([1]))
        {
            await _storage.UploadAsync("a/1.bin", s1, "application/octet-stream",
                cancellationToken: TestContext.Current.CancellationToken);
        }
        using (var s2 = new MemoryStream([1, 2]))
        {
            await _storage.UploadAsync("a/2.bin", s2, "application/octet-stream",
                cancellationToken: TestContext.Current.CancellationToken);
        }
        using (var s3 = new MemoryStream([1]))
        {
            await _storage.UploadAsync("b/3.bin", s3, "application/octet-stream",
                cancellationToken: TestContext.Current.CancellationToken);
        }

        var page = await _storage.ListAsync(
            prefix: "a/", cancellationToken: TestContext.Current.CancellationToken);

        page.Objects.Select(o => o.Key).Should().BeEquivalentTo(ExpectedPrefixAKeys);
    }

    [Fact]
    public async Task UploadAsync_RejectsPathTraversal()
    {
        using var stream = new MemoryStream([1]);
        await Assert.ThrowsAsync<ArgumentException>(
            () => _storage.UploadAsync("../escape.bin", stream, "application/octet-stream",
                cancellationToken: TestContext.Current.CancellationToken));
    }
}
