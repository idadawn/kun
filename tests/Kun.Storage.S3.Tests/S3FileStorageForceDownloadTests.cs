using Amazon.S3;
using Amazon.S3.Model;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Kun.Storage.S3.Tests;

/// <summary>
/// M1 guard test: every active web content type MUST trigger
/// <c>Content-Disposition: attachment</c> on the presigned download URL, and the
/// force flag must be recorded in <see cref="PresignedDownloadUrl.ContentDispositionForced"/>.
/// </summary>
public sealed class S3FileStorageForceDownloadTests
{
    [Theory]
    [InlineData("text/html")]
    [InlineData("image/svg+xml")]
    [InlineData("application/xhtml+xml")]
    [InlineData("application/xml")]
    public async Task GetPresignedDownloadAsync_ForcesAttachment_ForActiveWebTypes(string contentType)
    {
        var s3 = Substitute.For<IAmazonS3>();
        var head = new GetObjectMetadataResponse { ContentLength = 10, ETag = "abc", LastModified = DateTime.UtcNow };
        head.Headers.ContentType = contentType;

        s3.GetObjectMetadataAsync(
            Arg.Any<GetObjectMetadataRequest>(), Arg.Any<CancellationToken>())
          .Returns(head);

        GetPreSignedUrlRequest? captured = null;
        s3.GetPreSignedURLAsync(Arg.Do<GetPreSignedUrlRequest>(r => captured = r))
          .Returns("https://example.com/signed");

        var storage = new S3FileStorage(
            s3,
            Options.Create(new S3StorageOptions { BucketName = "bkt" }),
            NullLogger<S3FileStorage>.Instance,
            TimeProvider.System);

        var presigned = await storage.GetPresignedDownloadAsync(
            "uploads/hostile.xml",
            cancellationToken: TestContext.Current.CancellationToken);

        captured.Should().NotBeNull();
        captured!.ResponseHeaderOverrides.ContentDisposition.Should().StartWith("attachment;");
        captured.ResponseHeaderOverrides.ContentDisposition.Should().Contain("filename=\"");

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
        var s3 = Substitute.For<IAmazonS3>();
        var head = new GetObjectMetadataResponse { ContentLength = 10, LastModified = DateTime.UtcNow };
        head.Headers.ContentType = contentType;

        s3.GetObjectMetadataAsync(
            Arg.Any<GetObjectMetadataRequest>(), Arg.Any<CancellationToken>())
          .Returns(head);

        GetPreSignedUrlRequest? captured = null;
        s3.GetPreSignedURLAsync(Arg.Do<GetPreSignedUrlRequest>(r => captured = r))
          .Returns("https://example.com/signed");

        var storage = new S3FileStorage(
            s3,
            Options.Create(new S3StorageOptions { BucketName = "bkt" }),
            NullLogger<S3FileStorage>.Instance,
            TimeProvider.System);

        var presigned = await storage.GetPresignedDownloadAsync(
            "benign.png",
            cancellationToken: TestContext.Current.CancellationToken);

        captured.Should().NotBeNull();
        (captured!.ResponseHeaderOverrides.ContentDisposition ?? string.Empty).Should().BeEmpty();
        presigned.ContentDispositionForced.Should().BeFalse();
    }

    [Fact]
    public async Task GetPresignedDownloadAsync_RespectsCallerForceDownload_EvenOnBenignTypes()
    {
        var s3 = Substitute.For<IAmazonS3>();
        var head = new GetObjectMetadataResponse { ContentLength = 10, LastModified = DateTime.UtcNow };
        head.Headers.ContentType = "image/png";

        s3.GetObjectMetadataAsync(
            Arg.Any<GetObjectMetadataRequest>(), Arg.Any<CancellationToken>())
          .Returns(head);

        GetPreSignedUrlRequest? captured = null;
        s3.GetPreSignedURLAsync(Arg.Do<GetPreSignedUrlRequest>(r => captured = r))
          .Returns("https://example.com/signed");

        var storage = new S3FileStorage(
            s3,
            Options.Create(new S3StorageOptions { BucketName = "bkt" }),
            NullLogger<S3FileStorage>.Instance,
            TimeProvider.System);

        var presigned = await storage.GetPresignedDownloadAsync(
            "benign.png",
            new DownloadOptions(ForceDownload: true, DownloadFileName: "safe.png"),
            cancellationToken: TestContext.Current.CancellationToken);

        captured.Should().NotBeNull();
        captured!.ResponseHeaderOverrides.ContentDisposition.Should().StartWith("attachment;");
        captured.ResponseHeaderOverrides.ContentDisposition.Should().Contain("safe.png");

        // Not forced BY the active-web rule, but the caller asked for it:
        presigned.ContentDispositionForced.Should().BeFalse();
        presigned.ResponseHeaders.Should().ContainKey("Content-Disposition");
    }
}
