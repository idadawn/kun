<!-- Parent: ../AGENTS.md -->
<!-- Generated: 2026-04-22 | Updated: 2026-04-22 -->

# Kun.Storage.Abstractions

## Purpose

Transport-agnostic object-storage contract: `IFileStorage` with seven operations
(upload, presigned download / upload, existence, delete, metadata, list), records
describing presigned URLs, upload constraints, download options, and object metadata,
plus the `ActiveWebContentTypes` frozen set used by every implementation to enforce
`Content-Disposition: attachment` on rendered-web payloads.

## Key Files

| File | Description |
|------|-------------|
| `Kun.Storage.Abstractions.csproj` | `IsAotCandidate=true`, `IsPackable=true`, `HasPublicApi=true`. No third-party dependencies. |
| `IFileStorage.cs` | Seven-method contract matching SAD §6.4. |
| `ActiveWebContentTypes.cs` | `FrozenSet<string>` with **exactly four** entries: `text/html`, `image/svg+xml`, `application/xhtml+xml`, `application/xml`. Case-insensitive. |
| `PresignedUploadUrl.cs` / `PresignedDownloadUrl.cs` | Record types carrying the signed URL plus the headers the caller must send / the store will return. |
| `UploadConstraints.cs` | `MaxBytes` + `AllowedContentTypes` policy record. |
| `DownloadOptions.cs` | `ForceDownload` + `DownloadFileName` + custom response headers. |
| `ObjectMetadata.cs` / `ListObjectsResult.cs` | Read-side records. |
| `PublicAPI.Shipped.txt` / `PublicAPI.Unshipped.txt` | RS0016 public surface tracking. |

## For AI Agents

### Working In This Directory

- **AOT-safe**: `IsAotCandidate=true`. No reflection, no `Type.GetCustomAttributes`, no `Activator.CreateInstance`. All types are records / static classes / interfaces only — no runtime serialization lives here.
- **No third-party packages.** This module deliberately stays BCL + `Microsoft.Extensions.Logging.Abstractions` only. `AWSSDK.*` belongs exclusively in `Kun.Storage.S3`.
- **Do not add methods to `ActiveWebContentTypes.Value`.** The set is closed by M1 — any change requires updating the `ActiveWebContentTypesTests` matrix first and flagging the change in the plan.
- **Object keys are forward-slash-delimited store-relative paths.** Implementations are responsible for mapping to their native key conventions (S3 object key = this key verbatim; `LocalFileSystemStorage` replaces `/` with the OS directory separator under its root).

### Testing Requirements

Unit tests live in `tests/Kun.Storage.Abstractions.Tests`. The baseline test asserts
`ActiveWebContentTypes.Value` contains exactly the four M1-canonical types.

## Dependencies

### Internal

- _None_ — this module sits at the abstraction layer alongside `Kun.EventBus` and `Kun.Security`.

### External

- `Microsoft.Extensions.Logging.Abstractions` (for logger-message parity with callers; no runtime logging happens here).

<!-- MANUAL: -->
