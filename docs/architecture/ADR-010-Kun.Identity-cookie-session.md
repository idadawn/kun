# ADR-010: Add Cookie Session + `IAuthenticatedUser` to `Kun.Identity`

> Implementation update (S1-KUN-004): the [identity client guide](../guides/identity-client.md) and [canonical S2S guide](../security/s2s-jwt.md) supersede the legacy issuer and incomplete cookie-flow examples below. The original rationale is retained as history.

**Status**: Accepted
**Date**: 2026-04-24
**Author**: claude-kun (Kun framework specialist)
**Extends**: [ADR-008](./ADR-008-Kun.Identity-module.md) — *Introduce `Kun.Identity` Module*
**Supersedes**: none
**Context**: `neuroagent-csharp` Edge and Platform each ship an `/auth/callback?code=…` endpoint that exchanges an Authentik OIDC code for tokens, drops them into an ASP.NET Core cookie, and restores `HttpContext.User` on subsequent requests. The OIDC exchange now lives behind `Kun.Identity.IOidcClient` (ADR-008) but **the cookie session half is still hand-rolled per service** — each service calls `AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)` with subtly different options, invents its own `ISessionStore`-like shape (or worse, writes refresh tokens directly into the cookie), and exposes `ICurrentUser` via per-service adapters. The consumer drift `ADR-008` was supposed to close is re-emerging one layer up.

---

## What changed since ADR-008

Three concrete divergences drove this follow-up:

1. **Three near-duplicate cookie registrations.** Edge and Platform both
   call `services.AddAuthentication().AddCookie(...)` with the same
   Authentik issuer / audience / callback path, but with differing
   `ExpireTimeSpan`, `SlidingExpiration`, `Cookie.SameSite`, and
   `Cookie.SecurePolicy` values. None of them match the zero-trust
   defaults the framework wants to enforce.
2. **Session backend is whatever fits the demo.** Edge keeps tokens in
   the cookie payload (DataProtection-encrypted but large); Platform
   writes a `Dictionary<Guid, SessionRecord>` into `IMemoryCache` and
   calls it a session store. Neither is production-ready; neither
   agrees with the other on eviction policy.
3. **`ICurrentUser` is splintered.** `Kun.Core.Security.ICurrentUser`
   exposes `UserId`, `Roles`, `IsAuthenticated` — deliberately minimal,
   because it was designed for S2S JWT claims. The cookie / browser
   side needs `TenantId` (from the Authentik tenant claim),
   `Email`, `Name`, and direct access to the request's
   `ClaimsPrincipal` for downstream policy checks. Services currently
   cast to an ad-hoc `ICurrentUserEx` type they each define.

`ADR-008` consolidated external Bearer validation, S2S issuance, and
OIDC code exchange. The cookie session surface is the missing half of
the browser-facing flow.

---

## Decision

Extend `Kun.Identity` with a **cookie session surface** and introduce a
**new companion module** `Kun.Identity.AspNetCore` for the ASP.NET
Core middleware wiring. The abstractions stay in the core
`Kun.Identity` package so non-ASP.NET-Core hosts (gRPC-only, worker
services that resume a session from a replayed request, integration
tests that fake the cookie layer) can target them without pulling the
ASP.NET Core dependency.

### Abstractions added to `Kun.Identity`

```csharp
// Kun.Identity/Abstractions/Session/ISessionStore.cs
public interface ISessionStore
{
    Task<SessionData?> GetAsync(string sessionId, CancellationToken ct = default);
    Task SetAsync(string sessionId, SessionData data, TimeSpan ttl, CancellationToken ct = default);
    Task DeleteAsync(string sessionId, CancellationToken ct = default);
}

public sealed record SessionData(
    string UserId,
    string? TenantId,
    string? Email,
    string? Name,
    IReadOnlyList<string> Roles,
    string AccessToken,
    string? RefreshToken,
    DateTimeOffset ExpiresAt);

// Kun.Identity/Abstractions/IAuthenticatedUser.cs
// Extension of Kun.Core.Security.ICurrentUser — not a replacement.
public interface IAuthenticatedUser : ICurrentUser
{
    string? TenantId { get; }
    string? Email { get; }
    string? Name { get; }
    ClaimsPrincipal? Principal { get; }
}

// Kun.Identity/Configuration/KunCookieAuthOptions.cs
public sealed class KunCookieAuthOptions
{
    public string CookieName { get; set; } = "kun.sid";
    public string? Domain { get; set; }
    public string Path { get; set; } = "/";
    public bool HttpOnly { get; set; } = true;
    public SameSiteMode SameSite { get; set; } = SameSiteMode.Lax;
    public CookieSecurePolicy Secure { get; set; } = CookieSecurePolicy.Always;
    public TimeSpan SessionTtl { get; set; } = TimeSpan.FromHours(8);
    public bool SlidingExpiration { get; set; } = true;
}
```

### Implementations

| Type | Module | Notes |
|------|--------|-------|
| `InMemorySessionStore` | `Kun.Identity` | Dev / tests. Per-process `ConcurrentDictionary` with TTL sweep on read. |
| `RedisSessionStore` | `Kun.Identity` | Depends on `IKunCache` (`Kun.Caching`). Uses the **new reflection-free `GetBytesAsync` / `SetBytesAsync`** overload (ADR pre-requisite landed on `bd44a7f`) so the generic `[RequiresUnreferencedCode]` path is avoided entirely. Payloads serialized via a source-generated `SessionJsonContext`. Key shape: `kun:session:{sessionId}`. |
| `KunCurrentUser` | `Kun.Identity.AspNetCore` | `IAuthenticatedUser` implementation resolved per-request from `HttpContext.User`. |
| `UseKunCookieAuth` middleware | `Kun.Identity.AspNetCore` | Reads the cookie → `ISessionStore.GetAsync` → populates `HttpContext.User`. |
| `SignInHelper.SignInAsync` | `Kun.Identity.AspNetCore` | Exchanges an `OidcTokenResponse` → writes `SessionData` → emits the cookie. |
| `ChallengeHelper.BuildLoginRedirect` | `Kun.Identity.AspNetCore` | Builds the `/authorize` redirect (PKCE) against the registered `IOidcClient`. |

### DI entry point

```csharp
services.AddKunIdentity(id =>
{
    id.AddJwtBearer(...);                    // from ADR-008
    id.AddS2SIssuer(...);                    // from ADR-008
    id.AddOidcClient("authentik", ...);      // from ADR-008

    id.AddCookieSession(opts =>
    {
        opts.CookieName = "kun.sid";
        opts.SessionTtl = TimeSpan.FromHours(8);
        opts.UseRedisStore();    // default; swaps to .UseInMemoryStore() for tests/dev
    });
});

app.UseKunCookieAuth();
```

---

## Module shape and AOT

| Module | `IsAotCandidate` | Rationale |
|--------|------------------|-----------|
| `Kun.Identity` | `true` (unchanged from ADR-008) | Session store implementations and the new abstractions are reflection-free; `RedisSessionStore` serializes through a source-generated `SessionJsonContext`. |
| **`Kun.Identity.AspNetCore`** (new) | `true` | Middleware touches `HttpContext` + `ClaimsPrincipal` (all BCL); no reflection, no `JsonSerializer.Serialize(obj)`. AOT analyzer must stay clean; CI AOT matrix will include this module. |

**The `IKunCache` generic path stays out of the hot path.** The
Redis-backed session store calls `GetBytesAsync` / `SetBytesAsync`
exclusively; the generic `GetAsync<T>` / `SetAsync<T>` methods retain
their `[RequiresUnreferencedCode]` / `[RequiresDynamicCode]`
annotations untouched. See commit `bd44a7f` for the caching-side
pre-requisite.

---

## Why not put cookie middleware in `Kun.AspNetCore`?

`Kun.AspNetCore` today covers ProblemDetails, exception handling,
health checks, and endpoint routing helpers — cross-cutting concerns
that every Kun-hosted ASP.NET Core app wants. Cookie auth is
identity-scoped: it depends on `ISessionStore`, `IOidcClient`, the
`KunIdentityBuilder`, and the identity options graph. Mixing it into
`Kun.AspNetCore` would require that module to reference
`Kun.Identity`, which inverts the current dependency direction and
forces consumers that don't need auth to transitively drag identity
types into their closure.

A standalone `Kun.Identity.AspNetCore` keeps the layering clean:
`Kun.Identity.AspNetCore` → `Kun.Identity` → `Kun.Core`, with
`Microsoft.AspNetCore.App` as a framework reference only on the new
module.

---

## Why extend `ICurrentUser` rather than change it?

`Kun.Core.Security.ICurrentUser` is **shipped** (listed in
`Kun.Core/PublicAPI.Shipped.txt`) and used by the gateway's S2S path,
the audit module's author resolution, and any consumer reading the
current user from a background worker. Adding `TenantId` / `Email` /
`Principal` there would be a breaking API change for every consumer
that implements the interface manually — and `Kun.Core` is a BCL-only
module that must not take a dependency on `System.Security.Claims`
in its public contract just to satisfy a cookie-flow concern.

`IAuthenticatedUser : ICurrentUser` is the minimum-surface extension:
- S2S consumers keep using `ICurrentUser` and see nothing new.
- Browser-flow consumers depend on `IAuthenticatedUser` explicitly,
  receiving `TenantId` / `Email` / `Name` / `Principal` on top of the
  existing fields.
- The DI registration in `Kun.Identity.AspNetCore` registers
  `KunCurrentUser` as the implementation of **both**
  `ICurrentUser` and `IAuthenticatedUser`, so an endpoint can inject
  either and get the same request-scoped instance.

This is a strict superset — no breaking change, no cast chains.

---

## Consequences

### Positive

- Consumers replace per-repo cookie wiring with one `.AddCookieSession(...)`
  call — the drift driver that `ADR-008` closed for JWT / OIDC now
  also closes for the cookie half.
- `RedisSessionStore` is AOT-clean from day one because the serialization
  hop goes through a source-generated `JsonSerializerContext`, not the
  reflection-based `IKunCacheSerializer` default.
- `IAuthenticatedUser` gives browser-flow endpoints a typed handle on
  `ClaimsPrincipal` without forcing them to reach into `HttpContext.User`
  directly.
- Unblocks `kimi`'s `P0-AUTH-VERIFY` — the consumer will call
  `id.AddCookieSession(...)` + `app.UseKunCookieAuth()` rather than
  re-implementing the wiring inside `neuroagent-csharp/Identity/`.

### Negative / Trade-offs

- Framework surface grows by one module (`Kun.Identity.AspNetCore`)
  and ~6 public types (`ISessionStore`, `SessionData`,
  `IAuthenticatedUser`, `KunCookieAuthOptions`, `SignInHelper`,
  `ChallengeHelper`). The drift cost exceeds that.
- Two session stores coexist (InMemory for tests/dev, Redis for
  production). Consumers pick via fluent builder; the default when
  neither is selected is in-memory with a one-time warning log
  (consistent with `Kun.Caching`).
- The `Kun.Caching` pre-requisite (byte[] overload) is a permanent
  addition to `IKunCache` — callers other than `Kun.Identity` may
  start reaching for it. That's acceptable: it's a narrower, more
  AOT-friendly surface than the generic `T` path and the divergence
  between "cached DTOs" (`SetAsync<T>`) and "cached blobs"
  (`SetBytesAsync`) is intentional.

### Risks

| Risk | Mitigation |
|------|------------|
| Cookie forgery / session fixation | `KunCookieAuthOptions` defaults: `HttpOnly=true`, `Secure=Always`, `SameSite=Lax`, cookie value is an opaque 256-bit session id, not a serialized token. |
| Redis outage drops every logged-in user | `RedisSessionStore` can opt into `FallbackToMemoryOnRedisFailure` on the underlying `Kun.Caching` options, matching the pattern other Redis-backed modules already follow. Document the trade-off in the module AGENTS.md. |
| Session TTL drift between cookie and store | `SignInHelper` writes the same `SessionTtl` into both the cookie max-age and `ISessionStore.SetAsync` ttl argument in one call site. Single source of truth. |
| Claims drift on token refresh | Refresh-token support is out of scope for this ADR; when it lands, `SessionData.RefreshToken` is already part of the record so the refresh path can update `AccessToken` / `ExpiresAt` without reshaping the session. |

---

## Follow-ups

1. **Consume in `neuroagent-csharp` Edge + Platform** — replace the hand-rolled
   cookie registrations with `AddCookieSession(...)` + `UseKunCookieAuth()`.
   This is the P0-AUTH-VERIFY work that unblocks immediately after C5
   of this task lands.
2. **Add refresh-token rotation** — `KunOidcClient.ExchangeCodeAsync`
   already returns a `RefreshToken`; a future change can add a
   background refresh on `SessionData.ExpiresAt - skew`. Out of scope here.
3. **Sliding expiration edge cases** — current `SlidingExpiration = true`
   rewrites the cookie + Redis TTL on every authenticated request.
   If telemetry shows this is too write-heavy on Redis, gate the
   refresh by a configurable threshold (e.g. only rewrite when
   remaining TTL < 50% of `SessionTtl`).
4. **CSRF layer** — cookies + browser flow need CSRF tokens; that's
   a separate concern tracked by a future ADR once a second consumer
   needs it. In the interim, consumers should wire
   `AddAntiforgery(...)` explicitly.

---

## References

- Handoff: task brief `P1-KN-Identity-CookieSession`
- [ADR-008](./ADR-008-Kun.Identity-module.md) — the decision this ADR extends
- [ADR-005](./ADR-005-Kun.Caching-module.md) — the `IKunCache` contract the Redis store depends on
- `Kun.Core/Security/ICurrentUser.cs` — the shipped base interface `IAuthenticatedUser` extends
- `Kun.Core/PublicAPI.Shipped.txt` — the public-API commitment we avoid breaking by choosing extension over modification
- Commit `bd44a7f` — `feat(caching): add byte[] overload to IKunCache for session store` (the AOT pre-requisite that made this ADR implementable without softening the `IKunCache` generic contract)
