<!-- Parent: ../AGENTS.md -->
<!-- Generated: 2026-04-24 | Updated: 2026-04-24 -->

# Kun.Storage

## Purpose

Object-storage abstraction (`IFileStorage`) covering the seven operations required by downstream consumers: `PutObject`, `GetObject`, `HeadObject`, `DeleteObject`, `ListObjects`, `CreatePresignedUploadUrl`, `CreatePresignedDownloadUrl`. Ships two implementations — `S3FileStorage` (backed by AWSSDK.S3, also serves MinIO via path-style + custom endpoint) and `LocalFileStorage` (filesystem, dev-only with HMAC-signed `kun-local://` presigned URLs). Active-web MIME types (HTML, SVG, XML, JS, WASM) are forced to `Content-Disposition: attachment` at every write and presigned-download step so user-uploaded content cannot execute in the download origin.

## Key Files

| File | Description |
|------|-------------|
| `Kun.Storage.csproj` | `IsAotCandidate=false` — AWSSDK.S3 still relies on reflection; module is trim-safe only |
| `Abstractions/IFileStorage.cs` | The seven-method contract |
| `Abstractions/ContentDispositionPolicy.cs` | Active-web MIME whitelist + RFC 5987 attachment builder |
| `Implementations/S3FileStorage.cs` | S3 / MinIO backend |
| `Implementations/LocalFileStorage.cs` | Local filesystem backend with `.kunmeta` sidecar + HMAC-signed stub URLs |
| `DependencyInjection/KunStorageServiceCollectionExtensions.cs` | `AddKunStorage` / `AddLocalStorage` / `AddS3Storage` |

## For AI Agents

### Working In This Directory

- **Content-Disposition policy is non-negotiable.** Every write path and every presigned-download URL must pass through `ContentDispositionPolicy.EnforceAttachment` when the content-type is in the unsafe list. Do not add backend-specific shortcuts that bypass it.
- **The unsafe-MIME list is a security boundary.** Any addition or removal requires a brief note in the PR describing the threat model — do not adjust casually.
- **Forbidden in implementations**:
  - Trusting caller-supplied `Content-Disposition` without running it through the policy.
  - Accepting absolute paths or `..` segments in `LocalFileStorage` — `ResolvePath` enforces both a syntactic and a semantic (under-root) check.
  - Passing the `SecretKey` option straight to the local HMAC key — derive it via SHA-256 so stored keys don't leak as signing material.
- **`LocalFileStorage` is dev-only.** `kun-local://` URLs require a dedicated middleware to validate the HMAC and serve bytes; the scheme is not directly fetchable by a browser.
- **MetaData sidecar**: `LocalFileStorage` persists per-object metadata in `<path>.kunmeta` JSON. Delete it alongside the primary object.
- **`IsAotCandidate=false` is intentional.** AWSSDK.S3 trimming is shaky and AOT is untested. Do not flip without CI evidence from a publish gate.
- **Presigned TTL guidance**: keep `ExpiresIn` short — minutes, not hours. Long-lived links defeat the short-lifetime security story.
- **User metadata casing**: S3 lowercases user metadata keys. Store them lowercase to round-trip cleanly.

### Testing Requirements

- Unit tests cover the policy matrix, DI wiring, Local round-trips, and S3 behaviour with a mocked `IAmazonS3`. They must run without Docker.
- Integration tests (`tests/Kun.IntegrationTests/Storage/S3FileStorageIntegrationTests.cs`) spin up MinIO via Testcontainers and exercise: round-trip, attachment persistence, list-prefix, real presigned PUT via `HttpClient`, real presigned GET with attachment override.
- When adding a new backend, add a parallel unit-mock suite AND an integration fixture. Never let a production backend ship without container-level coverage.

### Common Patterns

```csharp
// Registering with explicit provider helpers is preferred over toggling options.Provider by hand.
services.AddS3Storage(o =>
{
    o.BucketName = "user-uploads";
    o.Endpoint   = "http://minio.internal:9000"; // MinIO
    o.AccessKey  = accessKey;
    o.SecretKey  = secretKey;
});

// Content-Disposition is enforced automatically — you do not need to pre-compute it.
await storage.PutObjectAsync(
    "users/alice/avatar.html",
    stream,
    new PutObjectOptions(ContentType: "text/html", FileName: "avatar.html"),
    ct);

// Stored disposition will be "attachment; filename=\"avatar.html\"; filename*=UTF-8''avatar.html"
```

## Dependencies

### Internal

- None from Kun.* — `Kun.Storage` is a self-contained infrastructure module.

### External

- `AWSSDK.S3` (3.7.x) — S3 / MinIO client.
- `AWSSDK.Extensions.NETCore.Setup` — DI helpers.
- `Microsoft.Extensions.DependencyInjection.Abstractions`, `Microsoft.Extensions.Options`, `Microsoft.Extensions.Logging.Abstractions` — wiring.

<!-- MANUAL: -->
