<!-- Parent: ../AGENTS.md -->
<!-- Generated: 2026-04-24 -->

# Kun.Identity.AspNetCore

## Purpose

ASP.NET Core integration layer for the `Kun.Identity` cookie-session
surface defined in [ADR-010](../../docs/architecture/ADR-010-Kun.Identity-cookie-session.md).
Kun.Identity itself stays framework-neutral (so gRPC-only / worker
hosts can construct a session from a replayed request and so the
session abstractions don't drag `Microsoft.AspNetCore.App` into the
framework-neutral package); this module adds the ASP.NET Core-specific
glue a browser-flow service needs:

| Type | Responsibility |
|------|----------------|
| `UseKunCookieAuth()` middleware | Read the session cookie on each request, resolve `ISessionStore.GetAsync`, populate `HttpContext.User`, and (optionally) rewrite the cookie + store entry when sliding expiration is on. |
| `KunCurrentUser` | `IAuthenticatedUser` (and `ICurrentUser`) implementation resolved per-request from `HttpContext.User`. Same instance for both interfaces — pick the injection site by which fields the caller needs. |
| `SignInHelper` | Takes an `OidcTokenResponse` (or a pre-built `SessionData`), writes the session to `ISessionStore`, and emits the cookie on the current `HttpContext`. |
| `ChallengeHelper` | Builds the `/authorize` redirect URL against a registered `IOidcClient`, including PKCE (`code_verifier`, `code_challenge`), `state`, and `nonce`. Stashes the one-shot values in a short-lived cookie so `/auth/callback` can validate them. |

## Layer and AOT

- Layer: **Host** (consumer of `Kun.Identity`). Depends on
  `Kun.Identity` + `Microsoft.AspNetCore.App` (framework reference).
- `<IsAotCandidate>true</IsAotCandidate>`. The middleware operates on
  `HttpContext` + `ClaimsPrincipal` only; no `JsonSerializer.Serialize(obj)`
  overloads, no `System.Reflection.Emit`, no runtime-generated types.
  `SignInHelper` round-trips `SessionData` through
  `Kun.Identity.SessionJsonContext`. If an ASP.NET Core upgrade
  regresses the AOT analyzer here, **stop-gate the upgrade** — do not
  silently flip `IsAotCandidate=false`, because the whole reason this
  module exists separately from `Kun.AspNetCore` (which is AOT=false)
  is to keep the cookie-auth path AOT-clean end-to-end.

## Security-sensitive defaults

`SignInHelper` and the middleware observe the zero-trust defaults
declared on `KunCookieAuthOptions`:

- `HttpOnly=true`, `Secure=Always`, `SameSite=Lax` by default.
- The cookie value is a 256-bit opaque session identifier generated
  via `RandomNumberGenerator.GetBytes` — **never** an access token,
  never a JWT.
- Sliding expiration rewrites both the cookie `Max-Age` and the
  `ISessionStore` entry TTL in one call site so a stale half never
  exists.
- `ChallengeHelper` writes the one-shot PKCE / state / nonce cookie
  with `HttpOnly=true`, `Secure=Always`, `SameSite=None` (the IdP's
  redirect is cross-site so Lax would drop the cookie) and a short
  TTL (5 minutes). Consumers that want to harden this further should
  wrap the value in `IDataProtector` at the callsite.

## Wiring

```csharp
services.AddKunIdentity(id =>
{
    id.AddOidcClient("authentik", opts => { ... });
    id.AddCookieSession(opts =>
    {
        opts.CookieName = "kun.sid";
        opts.SessionTtl = TimeSpan.FromHours(8);
        opts.UseRedisStore();   // or .UseInMemoryStore() for tests/dev
    });
});

app.UseKunCookieAuth();
// Your endpoints can now inject IAuthenticatedUser / ICurrentUser.

app.MapGet("/auth/login", async (HttpContext ctx, ChallengeHelper challenger, CancellationToken ct) =>
    Results.Redirect(await challenger.BuildLoginUrlAsync(ctx, "authentik", "/", ct)));

app.MapGet("/auth/callback", async (HttpContext ctx, IOidcClient oidc, SignInHelper signer, CancellationToken ct) =>
{
    var tokens = await oidc.ExchangeCodeAsync(code: ..., redirectUri: ..., ct);
    await signer.SignInAsync(ctx, tokens, ct);
    return Results.Redirect("/");
});
```

## For AI Agents

- The middleware is **read-only on `HttpContext.User`** until it
  resolves a session — do not write to `User` on anonymous requests,
  because downstream authentication middleware may legitimately set
  it from a different scheme (e.g. Bearer for API endpoints).
- `SignInHelper` writes the cookie directly onto
  `HttpContext.Response.Cookies`. Do not wrap the response body
  because the cookie header must reach the client on the redirect
  response, not after a 200.
- `ChallengeHelper` intentionally does not take a dependency on
  ASP.NET Core's built-in `AddAuthentication().AddOpenIdConnect(...)`
  handler — we do not want two code paths for OIDC. Consumers that
  need the built-in handler's automatic sign-out / claims-mapping
  features should wire it directly and skip `ChallengeHelper`.
- The AOT-strict constraint rules out `HttpContext.User = new ClaimsPrincipal(...)`
  built via reflection-based claims mapping. Construct the
  `ClaimsIdentity` explicitly from `SessionData` fields; do not pass
  the raw id-token through a generic JSON deserializer.
