# Kun.Sample.Storage

A minimal ASP.NET Core Minimal API that demonstrates `IFileStorage` wired to the
local-filesystem implementation `Kun.Storage.Local`. The sample focuses on a single
feature: the **M1 force-download rule** for active web content types.

## Running

```bash
dotnet run --project samples/Kun.Sample.Storage
```

The server listens on `http://localhost:5099` and stores objects under
`%TEMP%/kun-sample-storage`.

## Demo flow

### 1. Upload an HTML file (active web content type)

```bash
echo '<html><body><script>alert(1)</script></body></html>' > evil.html
curl -X POST \
     -H 'Content-Type: text/html' \
     --data-binary @evil.html \
     http://localhost:5099/api/files/evil.html
```

### 2. Request a presigned URL

```bash
curl http://localhost:5099/api/files/evil.html/presigned | jq
```

Expected response:

```json
{
  "url": "http://localhost:5099/storage/evil.html?expires=…&disposition=…",
  "expiresAt": "…",
  "forceDownloadApplied": true,
  "responseHeaders": {
    "Content-Disposition": "attachment; filename=\"evil.html\""
  }
}
```

Notice `forceDownloadApplied: true` — the sample code did **not** pass
`ForceDownload = true`. The implementation forced attachment disposition because
`text/html` is in `ActiveWebContentTypes.Value`.

### 3. Follow the URL

```bash
curl -v "http://localhost:5099/storage/evil.html?expires=…"
```

Response includes `Content-Disposition: attachment; filename="evil.html"`, so
browsers download the file instead of rendering (and executing) it.

### 4. Repeat for the other three active web content types

- `image/svg+xml` (inline `<script>` in SVG)
- `application/xhtml+xml`
- `application/xml`

All four must carry `Content-Disposition: attachment` on download.

## Contrast: a benign content type

```bash
curl -X POST -H 'Content-Type: image/png' --data-binary @logo.png \
     http://localhost:5099/api/files/logo.png
curl http://localhost:5099/api/files/logo.png/presigned
```

The response shows `forceDownloadApplied: false` — the browser renders `logo.png`
inline as expected.

## What the sample does not do

- No authentication; anyone who can reach the port can upload / download.
- The `/storage/{**path}` endpoint does not validate presigned-URL expiry or signatures.
  Production deployments replace this with a signed-URL-validating middleware or a
  dedicated file-serving service.
- No S3 backend — `Kun.Storage.S3` requires AWS credentials and is exercised in
  `tests/Kun.IntegrationTests/Storage/S3FileStorageMinioTests.cs` via a MinIO
  Testcontainer instead.
