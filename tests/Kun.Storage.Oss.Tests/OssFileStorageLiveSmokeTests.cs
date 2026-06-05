using System.Net.Http.Headers;
using Aliyun.OSS;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Kun.Storage.Oss.Tests;

/// <summary>
/// Live smoke test against a REAL Alibaba Cloud OSS bucket. Skipped unless all four
/// <c>OSS_SMOKE_*</c> environment variables are set, so it is inert in CI and for anyone
/// without credentials. It is the only thing that can verify OSS accepts the presigned
/// signature — no mock stands in for the OSS server.
/// </summary>
public sealed class OssFileStorageLiveSmokeTests
{
    private static (OssStorageOptions Options, bool Configured) ReadEnv()
    {
        var endpoint = Environment.GetEnvironmentVariable("OSS_SMOKE_ENDPOINT");
        var bucket = Environment.GetEnvironmentVariable("OSS_SMOKE_BUCKET");
        var ak = Environment.GetEnvironmentVariable("OSS_SMOKE_ACCESS_KEY_ID");
        var sk = Environment.GetEnvironmentVariable("OSS_SMOKE_ACCESS_KEY_SECRET");

        var configured =
            !string.IsNullOrWhiteSpace(endpoint) &&
            !string.IsNullOrWhiteSpace(bucket) &&
            !string.IsNullOrWhiteSpace(ak) &&
            !string.IsNullOrWhiteSpace(sk);

        return (new OssStorageOptions
        {
            Endpoint = endpoint ?? string.Empty,
            BucketName = bucket ?? string.Empty,
            AccessKeyId = ak ?? string.Empty,
            AccessKeySecret = sk ?? string.Empty,
        }, configured);
    }

    private static OssFileStorage CreateStorage(OssStorageOptions opts)
        => new(
            new OssClient(opts.Endpoint, opts.AccessKeyId, opts.AccessKeySecret),
            Options.Create(opts),
            NullLogger<OssFileStorage>.Instance);

    [Fact]
    public async Task PresignedRoundTrip_UploadThenDownload_AgainstRealOss()
    {
        var (opts, configured) = ReadEnv();
        Assert.SkipUnless(configured, "Set OSS_SMOKE_ENDPOINT / OSS_SMOKE_BUCKET / OSS_SMOKE_ACCESS_KEY_ID / OSS_SMOKE_ACCESS_KEY_SECRET to run.");

        var ct = TestContext.Current.CancellationToken;
        var storage = CreateStorage(opts);
        var key = $"iclaw-smoke/{Guid.NewGuid():N}.txt";
        var payload = $"iclaw oss smoke {Guid.NewGuid()}";
        using var http = new HttpClient();

        // 1. presigned PUT — the client uploads directly to OSS.
        var upload = await storage.GetPresignedUploadAsync(key, "text/plain", cancellationToken: ct);
        using (var put = new HttpRequestMessage(HttpMethod.Put, upload.Url))
        {
            put.Content = new StringContent(payload);
            put.Content.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
            var putResp = await http.SendAsync(put, ct);
            putResp.IsSuccessStatusCode.Should().BeTrue($"OSS rejected the presigned PUT: {putResp.StatusCode}");
        }

        // 2. server sees the object.
        (await storage.ExistsAsync(key, ct)).Should().BeTrue();

        // 3. presigned GET round-trips the exact bytes.
        var download = await storage.GetPresignedDownloadAsync(key, cancellationToken: ct);
        (await http.GetStringAsync(download.Url, ct)).Should().Be(payload);

        // 4. cleanup.
        (await storage.DeleteAsync(key, ct)).Should().BeTrue();
    }

    [Fact]
    public async Task ActiveWebContentType_ForcesAttachment_AgainstRealOss()
    {
        var (opts, configured) = ReadEnv();
        Assert.SkipUnless(configured, "Set OSS_SMOKE_* env vars to run.");

        var ct = TestContext.Current.CancellationToken;
        var storage = CreateStorage(opts);
        var key = $"iclaw-smoke/{Guid.NewGuid():N}.html";
        using var http = new HttpClient();

        var upload = await storage.GetPresignedUploadAsync(key, "text/html", cancellationToken: ct);
        using (var put = new HttpRequestMessage(HttpMethod.Put, upload.Url))
        {
            put.Content = new StringContent("<h1>hostile</h1>");
            put.Content.Headers.ContentType = new MediaTypeHeaderValue("text/html");
            (await http.SendAsync(put, ct)).EnsureSuccessStatusCode();
        }

        // The presigned GET must force attachment for active-web content (anti-XSS, M1).
        var download = await storage.GetPresignedDownloadAsync(key, cancellationToken: ct);
        download.ContentDispositionForced.Should().BeTrue();

        var getResp = await http.GetAsync(download.Url, ct);
        getResp.Content.Headers.ContentDisposition.Should().NotBeNull();
        getResp.Content.Headers.ContentDisposition!.DispositionType.Should().Be("attachment");

        await storage.DeleteAsync(key, ct);
    }
}
