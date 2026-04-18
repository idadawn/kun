<!-- Parent: ../AGENTS.md -->
<!-- Generated: 2026-04-18 | Updated: 2026-04-18 -->

# Kun.Gateway

## Purpose

YARP-based API gateway. Validates external IdP-issued JWTs from incoming client requests, mints short-lived S2S JWTs (via `Kun.Security.S2sTokenIssuer`) and replaces the Authorization header on forwarded requests, applies rate limiting (ASP.NET Core Rate Limiting with a Redis backend for distributed counters), and performs gRPC-Web → gRPC transcoding for browser clients that can't speak native gRPC.

## Key Files

| File | Description |
|------|-------------|
| `Kun.Gateway.csproj` | `IsAotCandidate=true` |

## For AI Agents

### Working In This Directory

- **This module issues S2S tokens; it does not trust any inbound S2S token.** Inbound traffic must present a valid *external* JWT (IdP-signed) or be rate-limited anonymous traffic for public endpoints.
- **YARP transform**: the `S2sAuthTransform` runs on every forwarded request; it reads `HttpContext.User`, asks `S2sTokenIssuer.Issue(principal, traceId)` for a token, and writes `Authorization: Bearer {token}` on the outbound request — replacing any existing Authorization header.
- **JWKS for S2S keys is served from this gateway** on `/internal/.well-known/jwks.json`, bound to the internal network only. Key rotation uses multiple `kid`s in the JWKS so consumers can accept the old and new key during the rollover window.
- **Rate limiting partitions** by (`TenantId`, `UserId`, route) where `TenantId`/`UserId` come from the *external* JWT claims. Anonymous traffic partitions by source IP.
- **gRPC-Web transcoding** is enabled per route; don't enable it globally — it complicates headers and framing for pure HTTP routes.

### Testing Requirements

Gateway-level tests (auth transform, rate limiter, JWKS rotation) belong in `tests/Kun.IntegrationTests`. CI's AOT publish pass includes this project — verify locally with `dotnet publish src/Kun.Gateway -c Release -p:PublishAot=true /warnaserror` when making non-trivial changes.

### Common Patterns

- YARP config in `appsettings.json`; dynamic config can use `IProxyConfigProvider` if needed for multi-tenant route overrides.
- Redis connection shared with the framework-wide Redis (used by HybridCache L2 and `IProcessedMessageStore`).

## Dependencies

### Internal

- `Kun.Security` — `S2sTokenIssuer`, authentication setup.

### External

- `Yarp.ReverseProxy`
- `Microsoft.AspNetCore.Authentication.JwtBearer`
- `StackExchange.Redis` (distributed rate limiter)

<!-- MANUAL: -->
