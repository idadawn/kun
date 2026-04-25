<!-- Parent: ../AGENTS.md -->
<!-- Generated: 2026-04-22 | Updated: 2026-04-22 -->

# Kun.Storage.S3

## Purpose

Amazon S3 / S3-compatible (MinIO, Ceph, LocalStack) implementation of `IFileStorage`.
Wraps the `AWSSDK.S3` v3.7 client with the Kun contract: seven methods, presigned URLs
with expiry, and the M1 force-download guarantee on active web content types.

## AOT posture — non-AOT by design (A6)

**`<IsAotCandidate>false</IsAotCandidate>`.** This module is the **first non-AOT library
module in the Kun repository.** The rationale:

1. `AWSSDK.S3` and its transitive `AWSSDK.Core` use runtime reflection for request /
   response marshalling (`Amazon.Runtime.Internal.Transform.*`). These are not trim- or
   AOT-safe in 3.7.x. A fully AOT-clean S3 client would require replacing the SDK with a
   bespoke SigV4 signer + HttpClient pipeline, which is out of scope for Sprint 1.
2. `AWSSDK.Extensions.NETCore.Setup` uses `Microsoft.Extensions.Configuration` binding
   (reflection-based) at DI wire-up time.

**Consequences:**

- Excluded from `.github/workflows/ci.yml`'s AOT-publish zero-warning matrix (see the
  `for proj in …` loop in the `aot-publish` job — this project is not listed).
- `dotnet publish src/Kun.Storage.S3 -c Release -p:PublishAot=true` is expected to emit
  trim/AOT warnings and is not run in CI.
- Consumers that need AOT must use `Kun.Storage.Local` or provide their own SigV4
  client-based implementation of `IFileStorage`.

A future roadmap note (tracked in `.omc/plans/open-questions.md`) flags a bespoke AOT S3
adapter as potential future work. Today, accept the trim-safe-but-not-AOT posture.

## Key Files

| File | Description |
|------|-------------|
| `Kun.Storage.S3.csproj` | `IsAotCandidate=false`. References `Kun.Storage.Abstractions`, `AWSSDK.S3`, `AWSSDK.Extensions.NETCore.Setup`. |
| `S3FileStorage.cs` | Seven-method `IFileStorage` implementation. Forces `Content-Disposition: attachment` on active-web-content-type downloads (M1). |
| `S3StorageOptions.cs` | `BucketName`, `ServiceUrl` (MinIO-friendly), `Region`, `ForcePathStyle`, `DefaultPresignedUrlLifetime`, optional static `AccessKey`/`SecretKey`. |
| `DependencyInjection/StorageServiceCollectionExtensions.cs` | `AddKunS3FileStorage(...)` — registers `IAmazonS3` + `IFileStorage` as singletons. |
| `PublicAPI.Shipped.txt` / `PublicAPI.Unshipped.txt` | RS0016 public surface tracking. |

## For AI Agents

### Working In This Directory

- **M1 force-download rule** is the critical invariant: `GetPresignedDownloadAsync` always
  reads stored `ContentType` via `GetObjectMetadataAsync` and, if it is one of the four
  entries in `ActiveWebContentTypes.Value`, sets
  `ResponseHeaderOverrides.ContentDisposition = "attachment; filename=…"` on the
  `GetPreSignedUrlRequest`. This holds even when `DownloadOptions.ForceDownload == false`.
- **MinIO compatibility**: set `S3StorageOptions.ServiceUrl` + `ForcePathStyle = true`.
  The integration test under `tests/Kun.IntegrationTests/Storage/S3FileStorageMinioTests.cs`
  exercises this configuration via `Testcontainers.Minio`.
- **No reflection beyond what AWSSDK already pulls in**. Do not add `Activator.CreateInstance`,
  `MakeGenericType`, or `Assembly.GetTypes` inside this module.
- **Logging**: use `[LoggerMessage]` source-gen delegates (CA1848) — the module inherits the
  repo-wide `AnalysisMode=AllEnabledByDefault`.

### Testing Requirements

- Unit tests (`tests/Kun.Storage.S3.Tests`): `S3FileStorageForceDownloadTests` — `[Theory]`
  over all four active web content types, asserting the presigned URL carries
  `response-content-disposition=attachment…`. Uses `Moto` / `NSubstitute` for the
  `IAmazonS3` mock.
- Integration tests (`tests/Kun.IntegrationTests/Storage/S3FileStorageMinioTests.cs`):
  Testcontainers-driven MinIO end-to-end upload → presigned download round-trip over all
  four active web types. Skips automatically on Docker-less hosts.

## Dependencies

### Internal

- `Kun.Storage.Abstractions`

### External

- `AWSSDK.S3` (3.7.x)
- `AWSSDK.Extensions.NETCore.Setup` (3.7.x)
- `Microsoft.Extensions.DependencyInjection.Abstractions`
- `Microsoft.Extensions.Logging.Abstractions`
- `Microsoft.Extensions.Options` / `.ConfigurationExtensions`

<!-- MANUAL: First non-AOT library module in the repo — deliberate exception to the repo-wide AOT-candidate default (open-questions.md A6). -->
