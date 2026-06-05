<!-- Parent: ../AGENTS.md -->
<!-- Generated: 2026-06-06 | Updated: 2026-06-06 -->

# Kun.Storage.Oss

## Purpose

Alibaba Cloud OSS implementation of `IFileStorage` using the native `Aliyun.OSS.SDK.NetCore`
client. Same seven-method contract as `Kun.Storage.S3`: presigned URLs with expiry and the M1
force-download guarantee on active web content types.

## Why native OSS, not the S3-compatible path

OSS exposes an S3-compatible API surface, but its presigned URLs are validated with OSS's own
signature algorithm — **not** AWS SigV4. An `AWSSDK.S3` client pointed at an OSS endpoint emits
SigV4-signed URLs that OSS rejects, so `GetPresignedUploadAsync` / `GetPresignedDownloadAsync`
(the core of the contract) do not work over `Kun.Storage.S3`. This module wraps the native SDK
and is the supported way to target OSS. MinIO / Ceph / AWS S3 continue to use `Kun.Storage.S3`.

## AOT posture — non-AOT by design

`<IsAotCandidate>false</IsAotCandidate>`, mirroring `Kun.Storage.S3`: `Aliyun.OSS.SDK.NetCore`
uses reflection-based marshalling and synchronous `HttpWebRequest` internals that are not trim-
or AOT-safe. Excluded from the AOT-publish zero-warning matrix.

## Async posture

The Aliyun SDK exposes only synchronous + APM (Begin/End) operations — no TAP. Network calls are
offloaded via `Task.Run` so the `IFileStorage` async surface holds; the `CancellationToken`
cancels queueing, not an in-flight OSS request. Presigned-URL generation is pure local signing
(no I/O) and runs synchronously.

## Key Files

| File | Description |
|------|-------------|
| `Kun.Storage.Oss.csproj` | `IsAotCandidate=false`. References `Kun.Storage.Abstractions`, `Aliyun.OSS.SDK.NetCore`. |
| `OssFileStorage.cs` | Seven-method `IFileStorage` implementation. Forces `Content-Disposition: attachment` on active-web-content-type downloads (M1). `GetPresignedPostUploadAsync` throws `NotSupportedException` (Phase 2). |
| `OssStorageOptions.cs` | `Endpoint`, `BucketName`, `AccessKeyId`, `AccessKeySecret`, `DefaultPresignedUrlLifetime`. |
| `DependencyInjection/StorageServiceCollectionExtensions.cs` | `AddKunOssFileStorage(...)` — registers `IOss` + `IFileStorage` as singletons. |
| `PublicAPI.Shipped.txt` / `PublicAPI.Unshipped.txt` | RS0016 public surface tracking. |

## For AI Agents

### Working In This Directory

- **M1 force-download rule** is the critical invariant: `GetPresignedDownloadAsync` reads stored
  `ContentType` via `GetObjectMetadata` and, when it is one of the four `ActiveWebContentTypes.Value`
  entries, sets `request.ResponseHeaders.ContentDisposition = "attachment; filename=…"`. Holds even
  when `DownloadOptions.ForceDownload == false`.
- **Credentials**: OSS has no ambient credential chain — `AccessKeyId` / `AccessKeySecret` are
  required. Source them from Infisical; never commit. STS tokens are a Phase 2 add.
- **POST-policy uploads** are intentionally unimplemented (`NotSupportedException`): OSS uses an
  HMAC-SHA1 POST policy distinct from S3, the iclaw upload path uses PUT presigned URLs, and an
  unverifiable signature implementation is worse than an explicit gap.
- **Logging**: `[LoggerMessage]` source-gen (CA1848).

### Testing Requirements

- Unit tests (`tests/Kun.Storage.Oss.Tests`): force-download decision over the four active web
  content types and options validation, using an `IOss` mock (NSubstitute). Presigned-signature
  correctness is verifiable only against a live OSS endpoint (see iclaw e2e smoke).

## Dependencies

### Internal

- `Kun.Storage.Abstractions`

### External

- `Aliyun.OSS.SDK.NetCore` (2.14.x)
- `Microsoft.Extensions.DependencyInjection.Abstractions`
- `Microsoft.Extensions.Logging.Abstractions`
- `Microsoft.Extensions.Options` / `.ConfigurationExtensions`
