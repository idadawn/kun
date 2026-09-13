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

The external JWT may use any algorithm the IdP supports. `KunGatewayOptions.ExternalJwksUri`
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

- A stolen token is useless within one minute of capture.
- There is no revocation list to maintain. Expiry is the revocation mechanism.
- Services re-acquire a fresh token on each outbound request via `PropagateS2sTokenHandler` — the
  latency overhead is zero because the handler copies the already-minted token from the incoming
  request header rather than calling the Gateway again.

`TokenValidationParameters.ClockSkew` is set to 30 seconds in `AddKunServiceAuthentication` to
tolerate minor clock drift between hosts. Effective window is therefore up to 90 seconds.

---

## Reusing the gateway host extensions

Kun.Gateway remains an executable host and can also be packed as a NuGet dependency.
Consumers can use its public AddKunGateway, S2sTokenIssuer and JWKS endpoint extensions
inside an application-specific host without copying the implementation. The consumer
still owns external authentication, route authorization and internal listener exposure.
A local package consumer is used to verify this distribution path before release.

## Explicit application context

The canonical gateway issuer can forward application-specific claim types using
`KunGatewayOptions.ForwardedClaimTypes`. The default is empty; session identifiers,
authorization revisions, permissions and personal data are not copied implicitly.

For a consumer that performs shared-session revocation checks, configure an explicit
allowlist, for example `["sid", "identity_revision", "permission"]`. The issuer copies
only those claim types from the principal returned by `IS2sPrincipalResolver`, after
the gateway has validated the external identity. This option never reads plain request
headers. Reserved issuer, audience, identity, role, trace and lifetime claims cannot be
added to the allowlist, including case variants; application configuration cannot replace
the internal token's trust boundary or expiry. The list is limited to 32 types, forwarded
values to 128 claims and 4,096 characters per value.

Role spellings `role`, `roles` and `ClaimTypes.Role` are normalized into distinct `roles`
claims. `JwtCurrentUser` recognizes the same spellings after token validation, so roles
survive a gateway-to-service round trip. The issuer emits a new `jti` for each token and a
`kid` matching the configured public JWKS. Lifetimes outside `(0, 300]` seconds are
rejected. The YARP transform awaits `IssueForAsync` and passes the request cancellation
token to the resolver rather than blocking a request thread.

Forwarding a session id or revision does not itself prove the session is still active.
The consuming identity boundary must validate those values against its durable authority,
and services must fail closed if that check is unavailable. This addition is a transport
contract, not a distributed session or permission store.

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

Key rotation replaces the Gateway's signing key pair without invalidating tokens that are
currently in-flight. The procedure relies on an overlap window.

**Step 1 — Generate a new key pair.**

```bash
# RSA 2048 or 4096
openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 -out new-private.pem
openssl rsa -in new-private.pem -pubout -out new-public.pem
```

**Step 2 — Add the new public key to the JWKS alongside the old one.**

The current `JwksEndpoint` implementation reads a single `PublicKeyPath`. To serve multiple keys
during the overlap window, update the endpoint to read both public keys and include both in the
`keys` array. Each key must have a distinct `kid`. The old key's `kid` remains in the response.

Downstream services will now accept tokens signed with either key, because `ValidAlgorithms`
allows RS256 from any key in the JWKS whose `kid` matches the token header.

**Step 3 — Switch the Gateway to sign with the new private key.**

Update `KunGatewayOptions.SigningKeyPath` to point at the new private key PEM and redeploy (or
hot-reload) the Gateway. From this point on, all newly issued tokens carry the new `kid`.

**Step 4 — Wait for TTL plus buffer.**

Wait at least `InternalTokenLifetime` (60s) plus a buffer (e.g. 30s) for all tokens signed with
the old key to expire. No token signed with the old key will be valid after this window.

**Step 5 — Remove the old public key from the JWKS.**

Update the JWKS endpoint to serve only the new public key. Tokens carrying the old `kid` will now
fail `kid` lookup and be rejected, but since all such tokens are past their TTL they would have
been rejected by `ValidateLifetime = true` anyway.

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

## NeuroAgent S2S JWT (Kun.Security.S2SJwt)

Alongside the Kun-canonical Gateway issuer documented above, the repository ships a second
implementation under `src/Kun.Security/S2SJwt/` that satisfies the NeuroAgent SAD v3.0 §12 claim
contract. The two implementations coexist because they answer different questions:

- `Kun.Gateway.S2sTokenIssuer` (Set A, the one described above) is the zero-trust primitive of the
  Kun framework itself — RS256, 60-second TTL, minted on every request as the external token is
  swapped for the internal one. It intentionally emits only the claims the framework needs.
- `Kun.Security.S2SJwt.S2SJwtIssuer` (Set B) is the NeuroAgent-specific issuer consumed by
  `neuroagent-csharp`. It uses ES256, a 15-minute default TTL, and the richer SAD §12 claim set
  below. It is **not** used by `Kun.Gateway` and does not replace Set A.

Use Set B when you are implementing a NeuroAgent service that must mint its own S2S tokens (the
edge gateway, offline workers, batch jobs). Use Set A when you are within the Kun request pipeline
behind `Kun.Gateway`.

### Claim shape (SAD v3.0 §12)

| Claim | Required | Source | Notes |
|---|:---:|---|---|
| `sub` | ✓ | `S2SJwtIssueRequest.UserId` | UUID of the authenticating user. |
| `jti` | ✓ | auto | Generated per-issue; `Guid.NewGuid("N")`. |
| `tenant_id` | ✓ | `S2SJwtIssueRequest.TenantId` | UUID of the owning tenant. |
| `aud` | ✓ | `S2SJwtIssueRequest.Audience` | Single value ⇒ string `aud`; multiple ⇒ payload `aud` array. |
| `roles` | — | `S2SJwtIssueRequest.Roles` | Each role is emitted as a distinct `roles` claim (not joined). |
| `scope` | — | `S2SJwtIssueRequest.Scopes` | Space-joined OAuth2-style scopes; emitted as a single `scope` claim when set. |
| `session_id` | — | `S2SJwtIssueRequest.SessionId` | Opaque session identifier for revocation support. |
| `membership_id` | — | `S2SJwtIssueRequest.MembershipId` | Active tenant membership, when a user has more than one. |
| `trace_id` | — | `S2SJwtIssueRequest.TraceId` | W3C trace id (hex) for OpenTelemetry trace continuity. |

### TTL and lifetime override

`S2SJwtOptions.DefaultLifetimeSeconds` defaults to **900** (15 minutes) per SAD §12. Individual
calls may shorten the window via `S2SJwtIssueRequest.Lifetime`, for example when minting a token
for a batch step that is expected to complete in under a minute.

### Registration

```csharp
// src/Kun.Security/S2SJwt/KunSecurityS2SJwtServiceCollectionExtensions.cs
services.AddS2SJwt(options =>
{
    options.SigningKeyPem = File.ReadAllText("keys/current.pem");
    options.KeyId         = "k1";
    options.Issuer        = "neuroagent-edge";
    // options.PreviousSigningKeyPem + PreviousKeyId for rotation
});
```

The `AddS2SJwt` extension registers both `IS2SJwtIssuer` and `IS2SJwtVerifier`, plus the JWKS
middleware that exposes the current public key at `/internal/.well-known/jwks.json` when invoked
via `app.UseS2SJwksEndpoint()`.

### Why two implementations and not one

- **Algorithm**: Kun framework treats RS256 as the lingua franca; NeuroAgent's platform contract
  standardised on ES256 before Kun's issuer existed.
- **Claim surface area**: NeuroAgent needs `membership_id` and multi-role RBAC; Kun's internal
  calls don't.
- **Lifetime**: Kun's 60-second TTL is a deliberate zero-trust primitive that pairs with
  `PropagateS2sTokenHandler` copying the inbound token unchanged — it would be wrong to push that
  to 15 minutes. NeuroAgent's 15-minute default is a product decision, not a framework decision.

Consolidating the two into one issuer is possible and may be revisited once the NeuroAgent claim
contract stabilises, but keeping them separate today makes the blast radius of each change small.
