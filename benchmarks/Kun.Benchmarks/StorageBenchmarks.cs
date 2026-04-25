using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using BenchmarkDotNet.Attributes;
using Kun.Storage.Abstractions;
using Kun.Storage.Configuration;
using Kun.Storage.Implementations;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Kun.Benchmarks;

/// <summary>
/// Storage-path baselines for <see cref="LocalFileSystemStorage"/> and
/// <see cref="S3FileStorage"/>.
///
/// <para>
/// <b>Presigned-URL comparison scope.</b>  <see cref="LocalFileSystemStorage"/>
/// intentionally throws <see cref="NotSupportedException"/> for presigned
/// URLs (it is a dev-only backend), so the "Local vs S3" comparison
/// targets only the cost S3 actually spends — HMAC-SHA256 canonical-request
/// signing. The AWS SDK performs that locally, with no network, so the
/// benchmark runs offline against an <see cref="AmazonS3Client"/>
/// instantiated with dummy credentials.
/// </para>
///
/// <para>
/// <b>PutObject / GetObject / HeadObject baselines</b> are measured on
/// <see cref="LocalFileSystemStorage"/> only — a fair S3 comparison here
/// requires a live endpoint, which is out of scope for a CPU-bound
/// benchmark.
/// </para>
/// </summary>
[MemoryDiagnoser]
public class StorageBenchmarks : IDisposable
{
    private LocalFileSystemStorage _local = null!;
    private AmazonS3Client _s3 = null!;
    private string _rootPath = null!;
    private byte[] _payload = null!;

    private const string ObjectKey = "bench/payload.bin";
    private const string S3Bucket = "kun-bench-fake";

    [GlobalSetup]
    public void Setup()
    {
        _rootPath = Path.Combine(Path.GetTempPath(), $"kun-bench-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_rootPath);

        _local = new LocalFileSystemStorage(
            Options.Create(new LocalStorageOptions { RootPath = _rootPath }),
            NullLogger<LocalFileSystemStorage>.Instance);

        // AmazonS3Client with fake credentials: GetPreSignedURL is a pure
        // local HMAC-SHA256 operation — the client never opens a socket.
        var creds = new BasicAWSCredentials("FAKE_AKID", "FAKE_SECRET");
        _s3 = new AmazonS3Client(creds, new AmazonS3Config
        {
            RegionEndpoint = RegionEndpoint.USEast1,
            UseHttp = true,
            // Use a bogus endpoint so no DNS resolution happens either.
            ServiceURL = "http://127.0.0.1:1/",
            ForcePathStyle = true,
        });

        _payload = new byte[1024];
        Random.Shared.NextBytes(_payload);

        // Seed a single object on disk so Get / Head benchmarks do not
        // measure a cold miss.
        using var seed = new MemoryStream(_payload);
        _local.PutObjectAsync(ObjectKey, seed, "application/octet-stream")
            .GetAwaiter().GetResult();
    }

    [GlobalCleanup]
    public void Cleanup() => Dispose();

    /// <summary>Disposes the S3 client and removes the temp root directory.</summary>
    public void Dispose()
    {
        _s3?.Dispose();
        if (!string.IsNullOrEmpty(_rootPath) && Directory.Exists(_rootPath))
        {
            try { Directory.Delete(_rootPath, recursive: true); }
            catch { /* best-effort */ }
        }
        GC.SuppressFinalize(this);
    }

    // ─── Local filesystem baselines ─────────────────────────────────────────

    [Benchmark(Baseline = true, Description = "Local · PutObject 1 KiB")]
    public async Task<ObjectMetadata> Local_PutObject_1KiB()
    {
        using var ms = new MemoryStream(_payload);
        return await _local.PutObjectAsync(ObjectKey, ms, "application/octet-stream").ConfigureAwait(false);
    }

    [Benchmark(Description = "Local · HeadObject")]
    public async Task<ObjectMetadata?> Local_HeadObject()
        => await _local.HeadObjectAsync(ObjectKey).ConfigureAwait(false);

    [Benchmark(Description = "Local · GetObject 1 KiB (copy to /dev/null)")]
    public async Task Local_GetObject_1KiB()
    {
        await using var stream = await _local.GetObjectAsync(ObjectKey).ConfigureAwait(false);
        await stream.CopyToAsync(Stream.Null).ConfigureAwait(false);
    }

    // ─── S3 presigned URL signing (pure-HMAC, offline) ──────────────────────

    [Benchmark(Description = "S3 · GetPreSignedURL (PUT)")]
    public string S3_PresignedPut()
    {
        var req = new GetPreSignedUrlRequest
        {
            BucketName = S3Bucket,
            Key = ObjectKey,
            Verb = HttpVerb.PUT,
            Expires = DateTime.UtcNow.AddMinutes(5),
            ContentType = "application/octet-stream",
        };
        return _s3.GetPreSignedURL(req);
    }

    [Benchmark(Description = "S3 · GetPreSignedURL (GET)")]
    public string S3_PresignedGet()
    {
        var req = new GetPreSignedUrlRequest
        {
            BucketName = S3Bucket,
            Key = ObjectKey,
            Verb = HttpVerb.GET,
            Expires = DateTime.UtcNow.AddMinutes(5),
        };
        return _s3.GetPreSignedURL(req);
    }
}
