<!-- Parent: ../AGENTS.md -->
<!-- Generated: 2026-04-18 | Updated: 2026-04-18 -->

# Kun.Gateway

## Purpose

YARP-based API gateway. Validates external IdP-issued JWTs from incoming client requests, mints short-lived S2S JWTs via `S2sTokenIssuer` and replaces the Authorization header on forwarded requests (via the internal `S2sAuthTransform`), applies rate limiting (ASP.NET Core Rate Limiting, tenant-partitioned by default), and exposes a JWKS endpoint so downstream services can validate the S2S tokens.

## Key Files

| File | Description |
|------|-------------|
| `Kun.Gateway.csproj` | `IsAotCandidate=true`, `Sdk=Microsoft.NET.Sdk.Web`, `OutputType=Exe` |
| `Program.cs` | Composition root: external JWT bearer + YARP + rate limiter + JWKS endpoint |
| `Configuration/KunGatewayOptions.cs` | Options for external IdP, internal issuer/audience, key file paths |
| `S2sTokenIssuer.cs` | Loads RSA private key from PEM; issues internal S2S JWTs (RS256, 60 s TTL) |
| `Transforms/S2sAuthTransform.cs` | Internal YARP `RequestTransform`; replaces `Authorization` header with minted S2S JWT |
| `Endpoints/JwksEndpoint.cs` | `MapKunJwks()` extension; serves `/.well-known/jwks.json` from PEM public key |
| `RateLimiting/IRateLimitPartitionResolver.cs` | Pluggable partition-key interface |
| `RateLimiting/Resolvers/TenantRateLimitPartitionResolver.cs` | Partitions by `tenant_id` claim; falls back to `"anonymous"` |
| `RateLimiting/Resolvers/IpRateLimitPartitionResolver.cs` | Partitions by `X-Forwarded-For` / `RemoteIpAddress` |
| `RateLimiting/KunRateLimiterExtensions.cs` | `AddKunRateLimiter()` — fixed-window 100 req/min, policy name `kun-default` |
| `DependencyInjection/KunGatewayServiceCollectionExtensions.cs` | `AddKunGateway()` — registers options, issuer, resolver, YARP with transform |
| `appsettings.json` | Skeleton config: KunGateway section + sample YARP routes/clusters |

## For AI Agents

### Working In This Directory

- **This module issues S2S tokens; it does not trust any inbound S2S token.** Inbound traffic must present a valid *external* JWT (IdP-signed) or be rate-limited anonymous traffic for public endpoints.
- **YARP transform**: the internal `S2sAuthTransform` runs on every forwarded request; it reads `HttpContext.User`, asks `S2sTokenIssuer.IssueFor(principal)` for a token, and writes `Authorization: Bearer {token}` on the outbound request — replacing any existing Authorization header.
- **JWKS for S2S keys** is served from `/.well-known/jwks.json`. The `kid` is a SHA-256 hash of the RSA modulus (first 16 Base64Url chars) for stable identification across restarts.
- **Rate limiting** partitions by `tenant_id` claim (default) or IP. Override by registering your own `IRateLimitPartitionResolver` singleton before `AddKunGateway`.
- **Do NOT use `AddKunServiceAuthentication`** here — that extension is for downstream services validating *incoming* S2S tokens. The gateway validates *external* IdP tokens directly via `AddJwtBearer`.
- **`S2sAuthTransform` is `internal sealed`** — it is wired by `AddKunGateway` and does not need to be referenced directly.

### Configuration

```json
{
  "KunGateway": {
    "ExternalIssuer": "https://login.example.com",
    "ExternalAudience": "my-api",
    "ExternalJwksUri": "https://login.example.com/.well-known/openid-configuration",
    "InternalIssuer": "kun-gateway",
    "InternalAudience": "kun-internal",
    "SigningKeyPath": "/etc/kun/gateway-private.pem",
    "PublicKeyPath": "/etc/kun/gateway-public.pem"
  }
}
```

### Testing Requirements

Gateway-level integration tests (auth transform, rate limiter, JWKS rotation) belong in `tests/Kun.IntegrationTests`. Unit tests for `S2sTokenIssuer`, `TenantRateLimitPartitionResolver`, and `IpRateLimitPartitionResolver` live in `tests/Kun.Gateway.Tests`.

CI's AOT publish pass includes this project — verify locally with:
```bash
dotnet publish src/Kun.Gateway -c Release -p:PublishAot=true /warnaserror
```

### Common Patterns

- YARP config in `appsettings.json`; dynamic config can use `IProxyConfigProvider` if needed.
- Custom rate limiting: register `IRateLimitPartitionResolver` before calling `AddKunRateLimiter()`.
- Key rotation: replace PEM files and restart; serve multiple `kid`s in JWKS via `FileKunJwksProvider` from `Kun.Security` if needed.

## Dependencies

### Internal

- `Kun.Core` — base types.
- `Kun.Security` — `S2sTokenOptions`, JWKS provider pattern.

### External

- `Yarp.ReverseProxy` (2.2.0)
- `Microsoft.AspNetCore.Authentication.JwtBearer` (10.0.0)
- `Microsoft.IdentityModel.Tokens` (8.2.0)

<!-- MANUAL: -->
