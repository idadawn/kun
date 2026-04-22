<!-- Parent: ../AGENTS.md -->
<!-- Generated: 2026-04-22 -->

# Kun.Storage

## Purpose

Object-storage abstraction covering Amazon S3, S3-compatible backends
(MinIO), and the local filesystem. Seven-method contract mandated by
`docs/neuroagent-phase1-gap.md §5.1`.

## Layer and AOT

- Layer: **Infrastructure** — implementations reference external SDKs
  (`AWSSDK.S3`) and filesystem APIs.
- `<IsAotCandidate>true</IsAotCandidate>` — the module intentionally
  avoids `TransferUtility`, `System.Reflection.Emit`, and any
  `JsonSerializer.Serialize(obj)` overloads so the AOT analyzer stays
  clean. If the AOT analyzer regresses (e.g. after an AWSSDK upgrade),
  **stop-gate the change, do not flip `IsAotCandidate=false` silently**
  — escalate to main for an ADR decision.

## Key Types

| Type | Responsibility |
|------|----------------|
| `IFileStorage` | 7-method contract: `PutObjectAsync`, `GetObjectAsync`, `HeadObjectAsync`, `DeleteObjectAsync`, `ListObjectsAsync`, `CreatePresignedUploadUrlAsync`, `CreatePresignedDownloadUrlAsync`. |
| `ObjectMetadata` | Immutable record: `ObjectKey`, `Size`, `ContentType`, `Sha256`, `LastModified`. |
| `PresignedUploadUrl` / `PresignedDownloadUrl` | Immutable records carrying the signed URL + expiry. |
| `S3FileStorage` | `AWSSDK.S3` implementation, MinIO-compatible when `S3StorageOptions.ForcePathStyle=true`. |
| `LocalFileSystemStorage` | Dev-only backend. Persists object body plus a `{key}.kun-meta` plain-text sidecar recording content type, SHA-256, size, and last-modified. |
| `ContentDispositionPolicy` (internal) | Single source of truth for the list of active-web MIME types that must be stored / served with `Content-Disposition: attachment`. |

## Security — Content-Disposition forcing

Active-web MIME types (`text/html`, `text/xml`, `application/xhtml+xml`,
`application/xml`, `image/svg+xml`,
`application/x-shockwave-flash`) are persisted to S3 with
`Content-Disposition: attachment; filename="…"` so that a compromised
object cannot execute in a browser served inline from the bucket. The
enforcement is symmetric: tests in `ContentDispositionPolicyTests`
assert the vocabulary is not silently trimmed.

The local backend stores these objects unchanged; it is not expected
to serve files over HTTP. Consumers that do expose local storage over
HTTP are responsible for replicating the `attachment` forcing at the
HTTP layer.

## Wiring

```csharp
services.AddKunStorage()
    .UseLocal(o => o.RootPath = "/var/lib/kun-storage");

// or:
services.AddKunStorage()
    .UseS3(o =>
    {
        o.BucketName = "kun-uploads";
        o.Endpoint = "http://minio:9000";
        o.Region = "us-east-1";
        o.AccessKey = "…";
        o.SecretKey = "…";
        o.ForcePathStyle = true;   // required for MinIO
    });
```

## For AI Agents

- Do not add new methods to `IFileStorage` without updating
  `docs/neuroagent-phase1-gap.md §5.1`. The gap audit is the
  authoritative AC; the interface exists to satisfy it.
- Do not add a third backend without a matching csproj split
  (`Kun.Storage.Azure`, etc.) — the core module deliberately stays
  AOT-strict and trim-safe.
- Extending `ContentDispositionPolicy.ActiveWebTypes` requires updating
  the tests in the same PR.
- When adding a method that hashes content, use `SHA256.HashData` or
  `SHA256.ComputeHashAsync` (reflection-free), never
  `HashAlgorithm.Create("SHA256")`.
