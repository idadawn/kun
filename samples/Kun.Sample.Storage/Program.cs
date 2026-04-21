using Kun.Storage;
using Kun.Storage.Local.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

// Register Kun.Storage.Local backed by a per-process temp directory.
// Real apps would bind to IConfiguration; here we keep the sample self-contained.
var storageRoot = Path.Combine(Path.GetTempPath(), "kun-sample-storage");
Directory.CreateDirectory(storageRoot);

builder.Services.AddKunLocalFileStorage(options =>
{
    options.RootPath = storageRoot;
    options.PublicBaseUrl = new Uri("http://localhost:5099/storage/", UriKind.Absolute);
    options.DefaultPresignedUrlLifetime = TimeSpan.FromMinutes(10);
});

var app = builder.Build();

// POST /api/files/{key}   content-type: <ct>   body: raw bytes
// Uploads a file to the backing store.
app.MapPost("/api/files/{key}", async (string key, HttpRequest request, IFileStorage storage, CancellationToken ct) =>
{
    var contentType = request.ContentType ?? "application/octet-stream";
    var meta = await storage.UploadAsync(key, request.Body, contentType, cancellationToken: ct);
    return Results.Ok(new { meta.Key, meta.Size, meta.ContentType, meta.ETag });
});

// GET /api/files/{key}/presigned
// Returns the presigned download URL. When the stored content type is an active web
// content type (text/html, image/svg+xml, application/xhtml+xml, application/xml) the
// response includes Content-Disposition: attachment and ContentDispositionForced=true,
// even though the caller did not set forceDownload=true. This is the M1 guarantee.
app.MapGet("/api/files/{key}/presigned", async (string key, IFileStorage storage, CancellationToken ct) =>
{
    var presigned = await storage.GetPresignedDownloadAsync(key, cancellationToken: ct);
    return Results.Ok(new
    {
        url = presigned.Url.ToString(),
        expiresAt = presigned.ExpiresAt,
        forceDownloadApplied = presigned.ContentDispositionForced,
        responseHeaders = presigned.ResponseHeaders,
    });
});

// GET /storage/{**path}
// Dev-only endpoint that serves the file bytes corresponding to a presigned URL.
// Production deployments would replace this with a signed-URL-validating middleware.
app.MapGet("/storage/{**path}", async (string path, IFileStorage storage, HttpContext ctx, CancellationToken ct) =>
{
    var meta = await storage.GetMetadataAsync(path, ct);
    if (meta is null)
    {
        return Results.NotFound();
    }

    var presigned = await storage.GetPresignedDownloadAsync(path, cancellationToken: ct);
    foreach (var (k, v) in presigned.ResponseHeaders)
    {
        ctx.Response.Headers[k] = v;
    }

    var full = Path.Combine(storageRoot, path.Replace('/', Path.DirectorySeparatorChar));
    return Results.File(full, meta.ContentType);
});

app.MapGet("/", () => Results.Text(
    """
    Kun.Sample.Storage — force-download demo.

    1) Upload an HTML blob:
       curl -X POST -H 'Content-Type: text/html' \
            --data-binary @evil.html \
            http://localhost:5099/api/files/evil.html

    2) Ask for a presigned URL:
       curl http://localhost:5099/api/files/evil.html/presigned

       The response shows forceDownloadApplied=true and
       responseHeaders contains Content-Disposition: attachment.

    3) Fetch the URL — the browser will save-as, not render.
    """));

app.Run();
