# Service-to-Service JWT Authentication

This document explains how Kun's zero-trust service mesh works, what the Gateway signs, what
downstream services verify, and how to rotate keys without dropping in-flight requests.

Audience: platform engineers wiring Kun services behind `Kun.Gateway`, and developers who need
to understand what identity information is available inside a handler.

---

## Two Key Pairs, Two Trust Boundaries

Kun maintains a strict separation between the public-facing identity layer and the internal
service mesh.

### External key pair (IdP-owned)

The external IdP — your OpenID Connect provider — issues JWTs to end users and clients. The
Gateway validates these tokens using the IdP's published JWKS. External tokens are accepted only
at `Kun.Gateway`; no downstream service ever sees or validates an external token directly.

The standalone gateway accepts RS256 and ES256 external JWTs. `KunGatewayOptions.ExternalJwksUri`
points to the IdP's JWKS endpoint. The Gateway fetches and caches keys from there.

### Internal key pair (Gateway-owned)

The Gateway holds its own RSA key pair. After validating an external token, the Gateway mints a
new short-lived internal token signed with its private key and forwards that token to downstream
services in place of the original. Downstream services validate against the Gateway's public JWKS,
which is served at `/.well-known/jwks.json` on the internal network only.

This means a downstream service never trusts anything that did not pass through the Gateway's
signing step. Plain headers (`X-User-Id`, `X-Tenant-Id`) are untrusted and must never be used
for authorization decisions.

---

## Why Zero-Trust Across the Service Mesh

A compromised component on the internal network cannot forge a valid S2S JWT without access to
the Gateway's RSA private key. The private key lives only on the Gateway host, loaded at startup
from `KunGatewayOptions.SigningKeyPath` as a PEM file. The corresponding public key is exposed via
JWKS only on the internal network interface.

Because every downstream service independently validates the JWT signature and checks the
`aud=kun-internal` audience claim, a forged or replayed token from outside the trust boundary is
rejected immediately at the JwtBearer middleware layer, before any handler code runs.

---

## Algorithm Constraint: RS256 or ES256 Only

HS256 (HMAC-SHA256) is explicitly rejected. The rejection is enforced in two places:

**Gateway issuance** (`src/Kun.Gateway/S2sTokenIssuer.cs`): the `S2sTokenIssuer` hardcodes
`SecurityAlgorithms.RsaSha256` when constructing `SigningCredentials`. The gateway always signs
with RS256.

**Downstream validation** (`src/Kun.Security/Authentication/KunServiceAuthenticationExtensions.cs`):
`AddKunServiceAuthentication` sets `TokenValidationParameters.ValidAlgorithms = ["RS256", "ES256"]`.
Any token signed with HS256 or another symmetric algorithm is rejected during signature
verification regardless of whether the signature itself is valid.

The reasoning: HS256 requires sharing a secret key between the issuer and every verifier. Sharing
a secret across many downstream services destroys the isolation property — any one compromised
service could forge tokens for all others. Asymmetric algorithms give each downstream service only
the public key, so compromise of one verifier does not enable forgery.

The `S2sTokenOptions` record documents this constraint explicitly:

```csharp
// src/Kun.Security/S2sTokenOptions.cs
public string SigningAlgorithm { get; init; } = "RS256";
// HS256 is explicitly forbidden — tokens signed with HS256 are rejected.
```

---

## Token Lifetime

The default TTL is **60 seconds**, set in both `S2sTokenOptions.Lifetime` and
`KunGatewayOptions.InternalTokenLifetime`. The short TTL is intentional:

- Configured lifetime must be between one and 300 seconds; expiry plus verifier clock skew bounds replay.
- Session/revision revocation remains an authority check; token expiry alone does not implement immediate user logout.
- `PropagateS2sTokenHandler` copies the existing bearer token. It does not mint or extend one, and must not be used to cross an audience boundary without a new authorized exchange.

`TokenValidationParameters.ClockSkew` is set to 30 seconds in `AddKunServiceAuthentication` to
tolerate minor clock drift between hosts. Effective window is therefore up to 90 seconds.

---

## Reusing the gateway host extensions

Kun.Gateway remains an executable host and can also be packed as a NuGet dependency.
Consumers can use its public AddKunGateway, S2sTokenIssuer and JWKS endpoint extensions
inside an application-specific host without copying the implementation. The consumer
still owns external authentication, route authorization and internal listener exposure.
A local package consumer is used to verify this distribution path before release.

## Canonical identity context

`S2sTokenIssuer` is the single supported issuer. It always writes one closed shape, including `aud` and `roles` arrays even when empty/single-valued. The current contract fixture is copied from [Qian contracts/v0](https://github.com/idadawn/qian/blob/5eaed840/contracts/v0/s2s-claims.schema.json) and tested through the real gateway HTTP boundary.

| Field | Issuance rule |
|---|---|
| `iss`, `aud` | Configured issuer and a single downstream audience; `IssueForAudienceAsync` selects one hop without changing global options. |
| `sub`, `principal_kind` | Authenticated subject; kind must explicitly be user or service. Agent/delegation issuance is reserved and rejected. |
| `tenant_id` | Explicit UUID or a claim with JSON-null value type for platform context. Absence is rejected. |
| `roles`, `scope` | Distinct coarse roles (maximum 32, each at most 64 chars) and bounded OAuth scope text. Roles do not replace permission decisions. |
| `session_id`, `jti` | Lowercase UUIDs; each token gets a fresh jti. A user supplies the authoritative session UUID; service session_id equals the new jti. |
| `membership_id`, `identity_revision` | User with a tenant requires membership UUID; every user requires a nonnegative revision. Service membership is rejected. |
| `project_id`, `plugin_id` | Optional UUID project and dotted component identifier. |
| `iat`, `nbf`, `exp` | Issuer-controlled numeric dates; caller claims cannot extend the 1–300 second lifetime. |

Use `IS2sPrincipalResolver` to project authoritative identity context after authentication. Do not fabricate missing session/revision/membership values or read them from plain headers. Existing opaque tenant identifiers must be migrated/mapped to the canonical UUID contract before upgrade. Missing/ambiguous required claims prevent issuance.

`ForwardedClaimTypes` remains a compatibility setting but only accepts canonical field names; it does not enable arbitrary extra claims. Email, raw provider sid, permission lists and trace_id are not emitted. `EmitTraceIdClaim` is retained but ignored; tracing uses W3C headers. A canonical session UUID is distinct from a provider's opaque sid and the local browser cookie.

The YARP transform awaits the resolver and honors request cancellation. Kernel issuance does not replace product-side session/revision validity checks, membership authorization, or per-service route policy.

---

## JWKS Endpoint

`Kun.Gateway` serves `/.well-known/jwks.json` via `JwksEndpoint.MapKunJwks` (see
`src/Kun.Gateway/Endpoints/JwksEndpoint.cs`). The response body is a standard JSON Web Key Set:

```json
{
  "keys": [
    {
      "kty": "RSA",
      "use": "sig",
      "kid": "abc123def456gh",
      "alg": "RS256",
      "n": "<base64url-encoded modulus>",
      "e": "AQAB"
    }
  ]
}
```

The `kid` is derived from the first 16 characters of the Base64-encoded SHA-256 hash of the RSA
modulus, making it stable across Gateway restarts for the same key pair.

This endpoint must be accessible only on the internal network. Do not expose it on a
public-facing interface. Downstream services are configured with the internal URL via
`KunServiceAuthenticationOptions.JwksUri`, and the built-in `ConfigurationManager` from
`Microsoft.IdentityModel.Protocols.OpenIdConnect` fetches and caches the keys automatically,
refreshing them when an unknown `kid` is encountered.

---

## Key Rotation

`AddKunGateway` uses one `S2sSigningKeyRing` for issuance and both JWKS endpoints. No endpoint code change or process restart is required:

1. Generate a new RSA private key of at least 2048 bits outside the process and provision it with restricted file permissions.
2. Atomically replace the PEM file at the existing `SigningKeyPath`. The next signing/JWKS access after `KeyReloadInterval` (default one second) validates the file and switches the signing key. Changing the configured path still requires host configuration/restart.
3. New tokens carry the new `kid`. The old **public** key remains in JWKS for `KeyOverlapPeriod` (default five minutes); the old private key is disposed immediately. Invalid/incomplete replacements retain the last valid key and emit a warning without logging key contents.
4. After the overlap expires, refreshed JWKS contains only current/unexpired overlap keys. No private RSA parameters are published. `PublicKeyPath` remains a compatibility input for standalone JWKS mapping without `AddKunGateway`; gateway rotation does not require changing that public file.

Set overlap to cover the longest token lifetime plus permitted consumer clock skew and JWKS refresh delay. Consumers must validate lifetime and refresh their key cache; removing a key from the endpoint cannot revoke copies already cached downstream. Both gateway JWKS routes send `Cache-Control: no-store`, which does not override a validator's internal cache. The real HTTP integration test verifies both signatures during overlap, and rejects the old signature against refreshed JWKS after expiry.

Overlap history is in memory. Complete rotation before restarting, and coordinate key provisioning across gateway replicas; a rolling restart or independent per-pod rotation is not a distributed key-retirement protocol. Missing initial keys still prevent signing in production. Development may generate local keys. Machine client-credentials authentication and credential storage remain responsibilities of the consuming identity authority; Kun provides the exchange/signing boundary, not a product identity database.

---

## Emergency Revocation

S2S tokens are stateless — there is no token blocklist. Emergency revocation does not mean
invalidating issued tokens; it means stopping new tokens from being issued.

If a downstream service or user account is compromised:

1. Revoke the external IdP token for the affected user or service account. The IdP's revocation
   mechanism (token introspection endpoint, session termination, or credential reset) prevents the
   Gateway from issuing new S2S tokens for that identity.
2. Wait 60 seconds (one TTL) for any in-flight S2S tokens to expire.
3. No token blocklist is needed, no restart of downstream services is required.

If the Gateway's private key itself is compromised, rotate the key pair immediately following the
procedure above (starting from Step 1). Generate a new key pair, serve both old and new public
keys, switch signing to the new key, and wait for old-signed tokens to expire.

---

## PropagateS2sTokenHandler

When service A calls service B over HTTP, it must forward the S2S JWT so service B can validate
the caller's identity. `PropagateS2sTokenHandler` (see `src/Kun.Security/Http/PropagateS2sTokenHandler.cs`)
is a `DelegatingHandler` that copies the `Authorization` header from the current inbound request
to every outbound request made by the `HttpClient` it wraps.

Register it on any named or typed `HttpClient` that calls a downstream Kun service:

```csharp
// In Program.cs or a service registration class
services.AddHttpClient<CatalogClient>(client =>
    {
        client.BaseAddress = new Uri("http://catalog");
    })
    .AddKunServiceAuth(); // src/Kun.Security/Extensions/HttpClientBuilderExtensions.cs
```

`AddKunServiceAuth` registers `PropagateS2sTokenHandler` as a transient service and chains it
into the `HttpClient` message handler pipeline. The handler reads
`IHttpContextAccessor.HttpContext.Request.Headers["Authorization"]` and copies the value to
`request.Headers.Authorization` on each outgoing call.

The handler only copies — it does not verify or re-sign. The token was already validated by the
JwtBearer middleware when the inbound request arrived. The 60-second TTL ensures the copied token
is still valid for the duration of the downstream call.

---

## What Downstream Services See

After `AddKunSecurity` and `AddKunServiceAuthentication` are registered, the following services
are available in DI:

### ITenantContext — JwtTenantContext

`src/Kun.Security/MultiTenancy/JwtTenantContext.cs` extracts `tenant_id` from the authenticated
`ClaimsPrincipal`:

```csharp
public string? TenantId =>
    _httpContextAccessor.HttpContext?.User?.FindFirst("tenant_id")?.Value;
```

Inject `ITenantContext` in any handler, repository, or service that needs to scope data by
tenant. `KunDbContext.OnModelCreating` applies a global query filter using `ITenantContext.TenantId`
automatically on any entity that implements `IMultiTenant` — so tenant isolation is enforced at
the EF Core query level without any per-handler code.

### ICurrentUser — JwtCurrentUser

`src/Kun.Security/Security/JwtCurrentUser.cs` reads `sub`, `name`, and `roles` claims:

```csharp
public string? UserId =>
    Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value
    ?? Principal?.FindFirst("sub")?.Value;

public IReadOnlyList<string> Roles => ...;
public bool IsAuthenticated => ...;
```

Inject `ICurrentUser` when a handler needs to know who is making the request, for example to
set `CreatedBy` on an audit entity.

### Registration

```csharp
// Minimal registration in Program.cs of a downstream service
builder.Services
    .AddKunSecurity()                                    // registers ITenantContext, ICurrentUser
    .AddAuthentication()
    .AddKunServiceAuthentication(opts =>
    {
        opts.Issuer   = "kun-gateway";
        opts.Audience = "kun-internal";
        opts.JwksUri  = "http://gateway/.well-known/jwks.json";
    });
```

`AddKunSecurity` is in `src/Kun.Security/DependencyInjection/KunSecurityServiceCollectionExtensions.cs`.
`AddKunServiceAuthentication` is in `src/Kun.Security/Authentication/KunServiceAuthenticationExtensions.cs`.

The order matters: `AddKunSecurity` must come before `AddAuthentication`, and `UseAuthentication`
must come before `UseAuthorization` in the middleware pipeline.

---

## Retired issuer APIs

`Kun.Identity.IJwtIssuer`/`KunJwtIssuer` and the `Kun.Security.S2SJwt` issuer/verifier/JWKS registration surface are obsolete (`KUNS2S001`). They remain only for binary/source migration compatibility. New code uses `Kun.Gateway.S2sTokenIssuer`, its shared JWKS ring and `Kun.Security` service authentication; do not introduce a second issuer or key lifecycle.

Migrate callers to authoritative canonical principal projection, per-service audience selection, and the closed payload described above. Do not copy old permission/email/trace fields or inherit an old long token lifetime. The identity browser client, atomic session registry and back-channel logout setup are covered in [the identity client guide](../guides/identity-client.md).
