<!-- Parent: ../AGENTS.md -->
<!-- Generated: 2026-04-22 -->

# Kun.Identity

## Purpose

Unified identity facade for Kun Framework consumers. The module
consolidates three configurations that every downstream service had
been hand-rolling — external Bearer-JWT validation (Authentik / other
OIDC IdPs), internal S2S token minting, and OIDC discovery + code
exchange — behind a single `AddKunIdentity(...)` builder.

The module adds three abstractions:

| Abstraction | Purpose |
|-------------|---------|
| `IJwtValidator` | Validate a Bearer token against a JWKS URI / static signing keys; return `ClaimsPrincipal?`. Framework-neutral — usable from ASP.NET Core, gRPC interceptors, or background workers. |
| `IJwtIssuer` | Mint a signed JWT for S2S communication. Replaces per-service hand-rolled issuers. |
| `IOidcClient` | Discover an OIDC provider's metadata and exchange an authorization code for tokens. Covers the code-flow half of OIDC; token-refresh is a follow-up. |

## Layer and AOT

- Layer: **Abstraction / Infrastructure**. Depends on `Kun.Core` +
  the Microsoft.IdentityModel stack.
- `<IsAotCandidate>true</IsAotCandidate>`. The module uses
  `JsonWebTokenHandler` (source-generated JSON paths in
  `Microsoft.IdentityModel.JsonWebTokens` 8.x) and
  `ConfigurationManager<OpenIdConnectConfiguration>` (reflection-free
  HTTP + metadata parsing). If an IdentityModel upgrade regresses
  the analyzer, **stop-gate the upgrade** — do not silently flip
  `IsAotCandidate=false`.

## Relationship to ADR-001 / Kun.Security

ADR-001 (Apr 2026) decided *against* a `Kun.Identity` module at the
time because `Kun.Gateway` + `Kun.Security` covered the Phase 1
needs. ADR-008 (this module) reverses that decision — not because
ADR-001 was wrong, but because consumer-side divergence has since
materialised: three business repositories each carry their own
`AddAuthentication().AddJwtBearer(...)` + per-service `S2SJwtIssuer`
+ `OpenIdConnectMessage` wiring, all slightly different. The
abstractions here pull that surface back into the framework.

The existing `Kun.Security.S2SJwt` types stay in place for backward
compatibility. `Kun.Identity.IJwtIssuer` provides a canonical
abstraction that new code should prefer; a future migration can
wire `Kun.Gateway.S2sTokenIssuer` and `Kun.Security.S2SJwtIssuer` to
implement the new interface rather than duplicating behaviour.

## Wiring

```csharp
services.AddKunIdentity(id =>
{
    id.AddJwtBearer(opts =>
    {
        opts.Issuer = "https://authentik.example.com";
        opts.Audience = "kun-api";
        opts.JwksUri = "https://authentik.example.com/application/o/kun/jwks/";
        opts.RequireExpirationTime = true;
        opts.ClockSkew = TimeSpan.FromMinutes(1);
    });

    id.AddOidcClient("authentik", opts =>
    {
        opts.Authority = "https://authentik.example.com/application/o/kun/";
        opts.ClientId = "kun-edge";
        opts.ClientSecret = Environment.GetEnvironmentVariable("AUTHENTIK_SECRET")!;
    });

    id.AddS2SIssuer(opts =>
    {
        opts.Issuer = "kun.internal";
        opts.Audience = "kun-internal";
        opts.SigningKeyPem = File.ReadAllText("/secrets/s2s-private.pem");
        opts.DefaultLifetime = TimeSpan.FromSeconds(60);
    });
});
```

## For AI Agents

- Do not wire ASP.NET Core `AddJwtBearer` from this module. Keep
  `IJwtValidator` framework-neutral so gRPC / worker hosts can call
  `ValidateAsync` without pulling in the ASP.NET Core auth pipeline.
  The consumer wires `AddJwtBearer` using `Kun.Security.AddKunServiceAuthentication`
  (for S2S flows) or their own bearer pipeline using `KunJwtBearerOptions`.
- The OIDC client only implements discovery + `authorization_code`
  exchange. Refresh-token, client-credentials, and device-flow
  grants are out of scope — add them in follow-up work rather than
  overloading this module.
- `KunJwtIssuer` signs with RSA keys loaded from PEM. Extending to
  EC keys or HSM-backed keys is a future option; keep the abstraction
  shape stable so we only add options, not interface methods.
- Do not re-export `System.IdentityModel.Tokens.Jwt.JwtSecurityToken`
  or `Microsoft.IdentityModel.Tokens.TokenValidationParameters` as
  public surface. Consumers use the POCO options and the module's
  interfaces; internals stay internal.
