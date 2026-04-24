using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using FluentAssertions;
using Kun.Storage.Abstractions;
using Kun.Storage.Implementations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Kun.Storage.Tests;

public sealed class S3FileStorageTests
{
    private readonly IAmazonS3 _s3;
    private readonly S3FileStorage _storage;
    private readonly CancellationToken _ct;

    public S3FileStorageTests()
    {
        _s3 = Substitute.For<IAmazonS3>();
        var options = Options.Create(new StorageOptions { BucketName = "test-bucket" });
        _storage = new S3FileStorage(_s3, options, Substitute.For<ILogger<S3FileStorage>>());
        _ct = TestContext.Current.CancellationToken;
    }

    [Fact]
    public async Task PutObject_Forces_Attachment_Disposition_For_Html()
    {
        PutObjectRequest? captured = null;
        _s3.PutObjectAsync(Arg.Do<PutObjectRequest>(r => captured = r), Arg.Any<CancellationToken>())
           .Returns(new PutObjectResponse());

        using var content = new MemoryStream(new byte[] { 1 });
        await _storage.PutObjectAsync(
            "evil.html",
            content,
            new PutObjectOptions(ContentType: "text/html", FileName: "evil.html"),
            _ct);

        captured.Should().NotBeNull();
        captured!.Headers.ContentDisposition.Should().NotBeNull();
        captured.Headers.ContentDisposition.Should().StartWith("attachment");
    }

    [Fact]
    public async Task PutObject_Does_Not_Set_Disposition_For_Safe_Mime()
    {
        PutObjectRequest? captured = null;
        _s3.PutObjectAsync(Arg.Do<PutObjectRequest>(r => captured = r), Arg.Any<CancellationToken>())
           .Returns(new PutObjectResponse());

        using var content = new MemoryStream(new byte[] { 1 });
        await _storage.PutObjectAsync(
            "pic.png",
            content,
            new PutObjectOptions(ContentType: "image/png"),
            _ct);

        captured.Should().NotBeNull();
        captured!.Headers.ContentDisposition.Should().BeNullOrEmpty();
    }

    [Fact]
    public async Task HeadObject_Returns_Null_On_NotFound()
    {
        _s3.GetObjectMetadataAsync(Arg.Any<GetObjectMetadataRequest>(), Arg.Any<CancellationToken>())
           .ThrowsAsync(new AmazonS3Exception("not found") { StatusCode = HttpStatusCode.NotFound });

        var result = await _storage.HeadObjectAsync("missing.txt", _ct);

        result.Should().BeNull();
    }

    [Fact]
    public async Task CreatePresignedDownloadUrl_Signs_Attachment_Override_For_Unsafe_Mime()
    {
        var metadataResp = new GetObjectMetadataResponse();
        metadataResp.Headers.ContentType = "text/html";
        _s3.GetObjectMetadataAsync(Arg.Any<GetObjectMetadataRequest>(), Arg.Any<CancellationToken>())
           .Returns(metadataResp);

        GetPreSignedUrlRequest? captured = null;
        _s3.GetPreSignedURL(Arg.Do<GetPreSignedUrlRequest>(r => captured = r))
           .Returns("https://s3.example.com/signed");

        var result = await _storage.CreatePresignedDownloadUrlAsync(
            "user/evil.html",
            new PresignedDownloadRequest(TimeSpan.FromMinutes(5)),
            _ct);

        captured.Should().NotBeNull();
        captured!.ResponseHeaderOverrides.ContentDisposition.Should().NotBeNull();
        captured.ResponseHeaderOverrides.ContentDisposition.Should().StartWith("attachment");
        result.Url.ToString().Should().Be("https://s3.example.com/signed");
    }

    [Fact]
    public async Task CreatePresignedDownloadUrl_Respects_Caller_Supplied_Disposition()
    {
        GetPreSignedUrlRequest? captured = null;
        _s3.GetPreSignedURL(Arg.Do<GetPreSignedUrlRequest>(r => captured = r))
           .Returns("https://s3.example.com/signed");

        await _storage.CreatePresignedDownloadUrlAsync(
            "report.pdf",
            new PresignedDownloadRequest(
                TimeSpan.FromMinutes(5),
                ResponseContentDisposition: "inline"),
            _ct);

        captured!.ResponseHeaderOverrides.ContentDisposition.Should().Be("inline");
        // And must not waste a HEAD round-trip when disposition is explicit.
        await _s3.DidNotReceive().GetObjectMetadataAsync(
            Arg.Any<GetObjectMetadataRequest>(), Arg.Any<CancellationToken>());
    }
}
