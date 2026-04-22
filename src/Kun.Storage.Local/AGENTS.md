<!-- Parent: ../AGENTS.md -->
<!-- Generated: 2026-04-22 | Updated: 2026-04-22 -->

# Kun.Storage.Local

## Purpose

Local-filesystem implementation of `IFileStorage`. Intended for development, tests, and
single-node deployments — **not** for multi-node production (no replication, no shared
state). Presigned download / upload URLs are developer-visible query-string-tokenized URLs;
validation and actual byte serving are host-app responsibilities (see the `Kun.Sample.Storage`
minimal API for a reference wiring).

## Key Files

| File | Description |
|------|-------------|
| `Kun.Storage.Local.csproj` | `IsAotCandidate=true`, `IsPackable=true`, `HasPublicApi=true`. Only references `Kun.Storage.Abstractions` + `Microsoft.Extensions.*`. |
| `LocalFileSystemStorage.cs` | Seven-method `IFileStorage` implementation. Writes a `.contenttype` sidecar next to each object to persist the declared MIME type. |
| `LocalFileStorageOptions.cs` | `RootPath` (abs. dir), `PublicBaseUrl` (presigned-URL prefix), `DefaultPresignedUrlLifetime`. |
| `DependencyInjection/StorageServiceCollectionExtensions.cs` | `AddKunLocalFileStorage(Action<LocalFileStorageOptions>)` — singleton registration. |
| `PublicAPI.Shipped.txt` / `PublicAPI.Unshipped.txt` | RS0016 public surface tracking. |

## For AI Agents

### Working In This Directory

- **AOT-safe**: `IsAotCandidate=true`. No reflection, no JSON contexts needed (no
  serialization). Uses `LoggerMessage` source-gen for all `ILogger<T>` calls (CA1848).
- **Force-download rule (M1)**: `GetPresignedDownloadAsync` sets the response
  `Content-Disposition: attachment` header when the stored content type is in
  `ActiveWebContentTypes.Value`, regardless of `DownloadOptions.ForceDownload`. This keeps
  Local's presigned URLs behaviorally equivalent to S3's force-download guarantee.
- **Presigned POST uploads are not supported here** (`GetPresignedPostUploadAsync` throws
  `NotSupportedException`). Signing a POST policy is an S3-specific construct — there is no
  meaningful way for a developer-local file store to enforce `content-length-range` against a
  signed policy. Dev scenarios should use `GetPresignedUploadAsync` (PUT); scenarios that
  need `MaxBytes` baked into the signature must swap to `Kun.Storage.S3` against a
  Minio/S3 endpoint. The Local impl intentionally does not synthesize a fake POST endpoint:
  a fake would either silently accept over-limit uploads (security regression) or diverge
  from the S3 wire format (dev/prod skew).
- **Object key validation**: keys are forward-slash-delimited relative paths. `..` segments
  and absolute paths are rejected to prevent traversal into parent directories.
- **Content-type persistence**: the declared MIME type is written to a sidecar file
  `{object-path}.contenttype`. Listing filters out `.contenttype` entries.
- **Pagination**: continuation tokens are opaque integer offsets. Callers must not parse or
  manipulate them.

### Testing Requirements

Unit tests live in `tests/Kun.Storage.Local.Tests`. The baseline test covers
upload → metadata read → presigned-download-URL shape → delete round-trip using a
throw-away temp directory.

## Dependencies

### Internal

- `Kun.Storage.Abstractions`

### External

- `Microsoft.Extensions.DependencyInjection.Abstractions`
- `Microsoft.Extensions.Logging.Abstractions`
- `Microsoft.Extensions.Options`
- `Microsoft.Extensions.Options.ConfigurationExtensions`

<!-- MANUAL: Not for multi-node production — no replication, single root path. -->
