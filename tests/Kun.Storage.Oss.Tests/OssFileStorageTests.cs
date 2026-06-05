using Aliyun.OSS;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;
using OssObjectMetadata = Aliyun.OSS.ObjectMetadata;

namespace Kun.Storage.Oss.Tests;

/// <summary>
/// Unit coverage for <see cref="OssFileStorage"/> over a mocked <see cref="IOss"/>. These verify
/// the framework-side logic (force-download decision, request shaping, validation, error mapping).
/// Presigned-signature correctness against a real OSS endpoint is covered by the iclaw e2e smoke,
/// since OSS validates the signature server-side and no mock can stand in for it.
/// </summary>
public sealed class OssFileStorageTests
{
    private static readonly string[] PngOnly = ["image/png"];

    private static OssFileStorage CreateStorage(IOss oss)
        => new(
            oss,
            Options.Create(new OssStorageOptions
            {
                Endpoint = "https://oss-cn-hangzhou.aliyuncs.com",
                BucketName = "bkt",
                AccessKeyId = "ak",
                AccessKeySecret = "sk",
            }),
            NullLogger<OssFileStorage>.Instance,
            TimeProvider.System);

    [Theory]
    [InlineData("text/html")]
    [InlineData("image/svg+xml")]
    [InlineData("application/xhtml+xml")]
    [InlineData("application/xml")]
    public async Task GetPresignedDownloadAsync_ForcesAttachment_ForActiveWebTypes(string contentType)
    {
        var oss = Substitute.For<IOss>();
        oss.GetObjectMetadata(Arg.Any<string>(), Arg.Any<string>())
           .Returns(new OssObjectMetadata { ContentType = contentType });

        GeneratePresignedUriRequest? captured = null;
        oss.GeneratePresignedUri(Arg.Do<GeneratePresignedUriRequest>(r => captured = r))
           .Returns(new Uri("https://example.com/signed"));

        var storage = CreateStorage(oss);

        var presigned = await storage.GetPresignedDownloadAsync(
            "uploads/hostile.xml",
            cancellationToken: TestContext.Current.CancellationToken);

        captured.Should().NotBeNull();
        captured!.ResponseHeaders.ContentDisposition.Should().StartWith("attachment;");
        captured.ResponseHeaders.ContentDisposition.Should().Contain("filename=\"");

        presigned.ContentDispositionForced.Should().BeTrue();
        presigned.ResponseHeaders.Should().ContainKey("Content-Disposition");
        presigned.ResponseHeaders["Content-Disposition"].Should().StartWith("attachment;");
    }

    [Theory]
    [InlineData("image/png")]
    [InlineData("application/pdf")]
    [InlineData("application/octet-stream")]
    public async Task GetPresignedDownloadAsync_DoesNotForceAttachment_ForBenignTypes(string contentType)
    {
        var oss = Substitute.For<IOss>();
        oss.GetObjectMetadata(Arg.Any<string>(), Arg.Any<string>())
           .Returns(new OssObjectMetadata { ContentType = contentType });

        GeneratePresignedUriRequest? captured = null;
        oss.GeneratePresignedUri(Arg.Do<GeneratePresignedUriRequest>(r => captured = r))
           .Returns(new Uri("https://example.com/signed"));

        var storage = CreateStorage(oss);

        var presigned = await storage.GetPresignedDownloadAsync(
            "benign.png",
            cancellationToken: TestContext.Current.CancellationToken);

        captured.Should().NotBeNull();
        (captured!.ResponseHeaders.ContentDisposition ?? string.Empty).Should().BeEmpty();
        presigned.ContentDispositionForced.Should().BeFalse();
    }

    [Fact]
    public async Task GetPresignedDownloadAsync_RespectsCallerForceDownload_EvenOnBenignTypes()
    {
        var oss = Substitute.For<IOss>();
        oss.GetObjectMetadata(Arg.Any<string>(), Arg.Any<string>())
           .Returns(new OssObjectMetadata { ContentType = "image/png" });

        GeneratePresignedUriRequest? captured = null;
        oss.GeneratePresignedUri(Arg.Do<GeneratePresignedUriRequest>(r => captured = r))
           .Returns(new Uri("https://example.com/signed"));

        var storage = CreateStorage(oss);

        var presigned = await storage.GetPresignedDownloadAsync(
            "benign.png",
            new DownloadOptions(ForceDownload: true, DownloadFileName: "safe.png"),
            cancellationToken: TestContext.Current.CancellationToken);

        captured.Should().NotBeNull();
        captured!.ResponseHeaders.ContentDisposition.Should().StartWith("attachment;");
        captured.ResponseHeaders.ContentDisposition.Should().Contain("safe.png");

        // Not forced BY the active-web rule, but the caller asked for it:
        presigned.ContentDispositionForced.Should().BeFalse();
        presigned.ResponseHeaders.Should().ContainKey("Content-Disposition");
    }

    [Fact]
    public async Task GetPresignedUploadAsync_UsesPutMethodAndSetsContentType()
    {
        var oss = Substitute.For<IOss>();
        GeneratePresignedUriRequest? captured = null;
        oss.GeneratePresignedUri(Arg.Do<GeneratePresignedUriRequest>(r => captured = r))
           .Returns(new Uri("https://example.com/put"));

        var storage = CreateStorage(oss);

        var presigned = await storage.GetPresignedUploadAsync(
            "uploads/new.png",
            "image/png",
            cancellationToken: TestContext.Current.CancellationToken);

        captured.Should().NotBeNull();
        captured!.Method.Should().Be(SignHttpMethod.Put);
        captured.ContentType.Should().Be("image/png");
        presigned.RequiredHeaders.Should().ContainKey("Content-Type");
        presigned.RequiredHeaders["Content-Type"].Should().Be("image/png");
    }

    [Fact]
    public async Task GetPresignedUploadAsync_RejectsDisallowedContentType()
    {
        var oss = Substitute.For<IOss>();
        var storage = CreateStorage(oss);

        var act = async () => await storage.GetPresignedUploadAsync(
            "uploads/x.exe",
            "application/x-msdownload",
            new UploadConstraints(AllowedContentTypes: PngOnly),
            cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task GetPresignedPostUploadAsync_ThrowsNotSupported()
    {
        var oss = Substitute.For<IOss>();
        var storage = CreateStorage(oss);

        var act = async () => await storage.GetPresignedPostUploadAsync(
            "uploads/x.png",
            cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<NotSupportedException>();
    }

    [Fact]
    public void Constructor_Throws_WhenBucketNameMissing()
    {
        var oss = Substitute.For<IOss>();
        var act = () => new OssFileStorage(
            oss,
            Options.Create(new OssStorageOptions { Endpoint = "https://x", AccessKeyId = "a", AccessKeySecret = "s" }),
            NullLogger<OssFileStorage>.Instance);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task ListAsync_Throws_WhenMaxKeysNonPositive()
    {
        var oss = Substitute.For<IOss>();
        var storage = CreateStorage(oss);

        var act = async () => await storage.ListAsync(
            maxKeys: 0,
            cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task ExistsAsync_DelegatesToDoesObjectExist()
    {
        var oss = Substitute.For<IOss>();
        oss.DoesObjectExist("bkt", "k").Returns(true);
        var storage = CreateStorage(oss);

        var exists = await storage.ExistsAsync("k", TestContext.Current.CancellationToken);

        exists.Should().BeTrue();
    }
}
