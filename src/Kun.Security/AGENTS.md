<!-- Parent: ../AGENTS.md -->
<!-- Generated: 2026-04-18 | Updated: 2026-04-18 -->

# Kun.Security

## Purpose

Internal service-to-service (S2S) authentication. The gateway signs short-lived S2S JWTs (60s TTL, RS256/ES256 asymmetric) and downstream services validate them via JWKS exposed only on the internal network. This module provides: `S2sTokenIssuer` (used by `Kun.Gateway`), `AddKunServiceAuthentication(...)` (used by downstream services), the HTTP message handler that propagates current-request S2S tokens to outbound calls, `JwtTenantContext` and `JwtCurrentUser` that map JWT claims to `ITenantContext` / `ICurrentUser`.

## Key Files

| File | Description |
|------|-------------|
| `Kun.Security.csproj` | `IsAotCandidate=true` |

## For AI Agents

### Working In This Directory

- **Asymmetric signing only** (RS256 / ES256). HMAC (HS256) is forbidden — symmetric keys would have to be shared with every downstream service, defeating the threat model.
- **External and internal keys are separate**: the external-JWT key pair (for IdP-issued user tokens, validated only at the gateway) must never sign S2S tokens, and vice versa.
- **Audience check enforces purpose**: S2S JWTs carry `aud = "kun-internal"`; downstream validators must reject tokens with any other audience. This prevents an external user token from being accepted as an S2S token.
- **TTL ≤ 5 minutes**, default 60 seconds. Do not add configuration to push this higher without a strong reason — the short lifetime is a deliberate mitigation against leaked tokens.
- **Never trust headers like `X-User-Id`**: identity comes from validated JWT claims only.
- **JWKS endpoint is internal**: serve it on a path like `/internal/.well-known/jwks.json` and bind it to the cluster-internal network only — never expose S2S public keys to the outside.

### Testing Requirements

Token issuance and validation round-trips are unit-testable without containers. Tests that verify the full gateway → downstream flow belong in `tests/Kun.IntegrationTests` with an in-memory TestServer for both.

### Common Patterns

- Claim shape: `iss=kun-gateway`, `aud=kun-internal`, `sub`, `tenant_id`, `roles[]`, `jti`, `trace_id`, `original_token_jti`, `exp`, `iat`, `nbf`.
- `JwtTenantContext` reads `tenant_id` claim; `JwtCurrentUser` reads `sub` and `roles[]`.

## Dependencies

### Internal

- `Kun.Core` — `ITenantContext`, `ICurrentUser`.

### External

- `System.IdentityModel.Tokens.Jwt`
- `Microsoft.IdentityModel.Protocols.OpenIdConnect`
- `Microsoft.AspNetCore.Authentication.JwtBearer`

<!-- MANUAL: -->

`AddKunSecurity` 当前将 `ITenantContext` 注册为 `AsyncLocalTenantContext`，`JwtTenantContext` 仅以 `ITenantIdSource` 提供已认证的 `tenant_id`；显式后台作用域优先。
