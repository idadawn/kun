using System.Text;
using System.Text.Json;
using Amazon.S3;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Kun.Storage.S3.Tests;

/// <summary>
/// Unit tests for <see cref="S3FileStorage.GetPresignedPostUploadAsync"/>. The critical
/// guarantee under test: when <see cref="UploadConstraints.MaxBytes"/> is set, a
/// <c>content-length-range</c> condition is embedded in the signed policy, so S3 rejects
/// oversized uploads at the edge (HTTP 400) without any bytes landing in the bucket.
/// </summary>
public sealed class S3FileStoragePresignedPostTests
{
    private const string TestBucket = "bkt";
    private const string TestAccessKey = "AKIAIOSFODNN7EXAMPLE";
    private const string TestSecretKey = "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY";
    private const string TestRegion = "us-east-1";

    private static readonly string[] SinglePngContentType = ["image/png"];
    private static readonly string[] MultipleContentTypes = ["image/png", "image/jpeg"];

    private static S3FileStorage CreateStorage(TimeProvider? timeProvider = null)
    {
        var s3 = Substitute.For<IAmazonS3>();
        return new S3FileStorage(
            s3,
            Options.Create(new S3StorageOptions
            {
                BucketName = TestBucket,
                AccessKey = TestAccessKey,
                SecretKey = TestSecretKey,
                Region = TestRegion,
            }),
            NullLogger<S3FileStorage>.Instance,
            timeProvider ?? TimeProvider.System);
    }

    [Fact]
    public async Task GetPresignedPostUploadAsync_ReturnsRequiredFormFields()
    {
        var storage = CreateStorage();

        var result = await storage.GetPresignedPostUploadAsync(
            "uploads/file.bin",
            constraints: new UploadConstraints(MaxBytes: 1024),
            expiresIn: TimeSpan.FromMinutes(5),
            cancellationToken: TestContext.Current.CancellationToken);

        result.FormFields.Should().ContainKey("key").WhoseValue.Should().Be("uploads/file.bin");
        result.FormFields.Should().ContainKey("policy");
        result.FormFields.Should().ContainKey("x-amz-signature");
        result.FormFields.Should().ContainKey("x-amz-algorithm").WhoseValue.Should().Be("AWS4-HMAC-SHA256");
        result.FormFields.Should().ContainKey("x-amz-credential");
        result.FormFields.Should().ContainKey("x-amz-date");
        result.FormFields["x-amz-credential"].Should()
            .Contain(TestAccessKey).And.Contain(TestRegion).And.Contain("/s3/aws4_request");
        result.Url.IsAbsoluteUri.Should().BeTrue();
    }

    [Fact]
    public async Task GetPresignedPostUploadAsync_EncodesContentLengthRange_WhenMaxBytesSet()
    {
        var storage = CreateStorage();

        var result = await storage.GetPresignedPostUploadAsync(
            "uploads/file.bin",
            constraints: new UploadConstraints(MaxBytes: 8_000_000),
            cancellationToken: TestContext.Current.CancellationToken);

        var policy = DecodePolicy(result.FormFields["policy"]);
        var conditions = policy.RootElement.GetProperty("conditions");

        var range = conditions.EnumerateArray()
            .Where(e => e.ValueKind == JsonValueKind.Array &&
                        e.GetArrayLength() == 3 &&
                        e[0].ValueKind == JsonValueKind.String &&
                        e[0].GetString() == "content-length-range")
            .ToList();

        range.Should().HaveCount(1, "content-length-range must appear exactly once when MaxBytes is set");
        range[0][1].GetInt64().Should().Be(0);
        range[0][2].GetInt64().Should().Be(8_000_000);
    }

    [Fact]
    public async Task GetPresignedPostUploadAsync_OmitsContentLengthRange_WhenMaxBytesNull()
    {
        var storage = CreateStorage();

        var result = await storage.GetPresignedPostUploadAsync(
            "uploads/file.bin",
            constraints: new UploadConstraints(MaxBytes: null),
            cancellationToken: TestContext.Current.CancellationToken);

        var policy = DecodePolicy(result.FormFields["policy"]);
        var conditions = policy.RootElement.GetProperty("conditions");

        conditions.EnumerateArray()
            .Any(e => e.ValueKind == JsonValueKind.Array &&
                      e.GetArrayLength() > 0 &&
                      e[0].ValueKind == JsonValueKind.String &&
                      e[0].GetString() == "content-length-range")
            .Should().BeFalse("content-length-range must not appear when MaxBytes is null");
    }

    [Fact]
    public async Task GetPresignedPostUploadAsync_EncodesContentTypeEquality_WhenSingleAllowedType()
    {
        var storage = CreateStorage();

        var result = await storage.GetPresignedPostUploadAsync(
            "uploads/file.png",
            constraints: new UploadConstraints(
                MaxBytes: 1024,
                AllowedContentTypes: SinglePngContentType),
            cancellationToken: TestContext.Current.CancellationToken);

        result.FormFields.Should().ContainKey("Content-Type").WhoseValue.Should().Be("image/png");

        var policy = DecodePolicy(result.FormFields["policy"]);
        var conditions = policy.RootElement.GetProperty("conditions");

        conditions.EnumerateArray()
            .Any(e => e.ValueKind == JsonValueKind.Object &&
                      e.TryGetProperty("Content-Type", out var ct) &&
                      ct.GetString() == "image/png")
            .Should().BeTrue("the policy must include a Content-Type equality condition");
    }

    [Fact]
    public async Task GetPresignedPostUploadAsync_Throws_WhenMultipleAllowedContentTypes()
    {
        var storage = CreateStorage();

        var act = () => storage.GetPresignedPostUploadAsync(
            "uploads/file.bin",
            constraints: new UploadConstraints(
                MaxBytes: 1024,
                AllowedContentTypes: MultipleContentTypes),
            cancellationToken: TestContext.Current.CancellationToken);

        var ex = await Assert.ThrowsAsync<NotSupportedException>(act);
        ex.Message.Should().Contain("single entry", "the remediation must point to the allowed workaround");
    }

    [Fact]
    public async Task GetPresignedPostUploadAsync_Throws_WhenCredentialsMissing()
    {
        var s3 = Substitute.For<IAmazonS3>();
        var storage = new S3FileStorage(
            s3,
            Options.Create(new S3StorageOptions
            {
                BucketName = TestBucket,
                Region = TestRegion,
                // no AccessKey / SecretKey
            }),
            NullLogger<S3FileStorage>.Instance,
            TimeProvider.System);

        var act = () => storage.GetPresignedPostUploadAsync(
            "uploads/file.bin",
            cancellationToken: TestContext.Current.CancellationToken);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(act);
        ex.Message.Should().Contain("AccessKey").And.Contain("SecretKey");
    }

    [Fact]
    public async Task GetPresignedPostUploadAsync_ProducesDeterministicSignature_ForFixedInputs()
    {
        // Fixed clock ensures amzDate (and therefore the signing-key chain and signed policy)
        // is deterministic — the signature output is the same across runs when all inputs are.
        var fixedNow = new DateTimeOffset(2026, 4, 22, 12, 0, 0, TimeSpan.Zero);
        var timeProvider = new FixedTimeProvider(fixedNow);

        var storageA = CreateStorage(timeProvider);
        var storageB = CreateStorage(timeProvider);

        var resultA = await storageA.GetPresignedPostUploadAsync(
            "uploads/file.bin",
            constraints: new UploadConstraints(MaxBytes: 1024),
            expiresIn: TimeSpan.FromMinutes(15),
            cancellationToken: TestContext.Current.CancellationToken);

        var resultB = await storageB.GetPresignedPostUploadAsync(
            "uploads/file.bin",
            constraints: new UploadConstraints(MaxBytes: 1024),
            expiresIn: TimeSpan.FromMinutes(15),
            cancellationToken: TestContext.Current.CancellationToken);

        resultA.FormFields["x-amz-signature"].Should().Be(resultB.FormFields["x-amz-signature"]);
        resultA.FormFields["policy"].Should().Be(resultB.FormFields["policy"]);
        resultA.FormFields["x-amz-date"].Should().Be(resultB.FormFields["x-amz-date"]);
        resultA.FormFields["x-amz-credential"].Should().Be(resultB.FormFields["x-amz-credential"]);
        resultA.FormFields["x-amz-signature"].Should().MatchRegex("^[0-9a-f]{64}$", "SigV4 signatures are 64 lowercase hex chars");
    }

    private static JsonDocument DecodePolicy(string base64Policy)
    {
        var json = Encoding.UTF8.GetString(Convert.FromBase64String(base64Policy));
        return JsonDocument.Parse(json);
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public FixedTimeProvider(DateTimeOffset now) => _now = now;

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
