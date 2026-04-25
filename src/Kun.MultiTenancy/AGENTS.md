<!-- Parent: ../AGENTS.md -->
<!-- Generated: 2026-04-22 -->

# Kun.MultiTenancy

## Purpose

Tenant-resolution pipeline for Kun Framework consumers. The
`ITenantContext` contract already lives in `Kun.Core`
(`Kun.Core.MultiTenancy.ITenantContext`); this module adds:

- `ITenantResolver` — chain-of-responsibility abstraction that
  produces a tenant id from the current `HttpContext`.
- `DefaultTenantResolver` — options-driven resolver consulting (in
  order) one or more JWT claim names, one or more request headers,
  and finally a configured fallback.
- `AsyncLocalTenantContext` — `ITenantContext` backed by an
  `AsyncLocal<string?>` so the tenant id flows with the async call
  graph regardless of thread hops.
- `TenantContextMiddleware` + `UseKunMultiTenancy` — plumb the
  resolver into the AsyncLocal scope for the duration of the
  request.
- `AddKunMultiTenancy(Action<KunMultiTenancyOptions>)` — single DI
  entry point.

EF Core integration (query filter + SaveChanges interceptor that
auto-fills `TenantId` on insert) lives in the sibling
`Kun.MultiTenancy.EntityFrameworkCore` module — that module is
`IsAotCandidate=false` because EF Core 10 is not AOT-strict. See
ADR-007 for the split decision.

## Layer and AOT

- Layer: **Abstraction / Host-adjacent**. Depends on `Kun.Core` plus
  the `Microsoft.AspNetCore.App` shared framework.
- `<IsAotCandidate>true</IsAotCandidate>`. The resolver walks the
  configured claim / header names via `HttpContext` APIs only — no
  reflection, no runtime type discovery. If an ASP.NET Core upgrade
  regresses the analyzer, **stop-gate the upgrade**; do not flip the
  module to `false` silently.

## Canonical tenant string

The existing `Kun.Core.MultiTenancy.ITenantContext.TenantId` is
`string?` — this module stays on the string shape. JWT `tid`
claims, `X-Tenant-Id` header values, and downstream `TenantId`
columns are all carried as strings so that Guid / integer / slug
tenants coexist without framework changes. The SaveChanges
interceptor treats `null` or empty strings as "not yet assigned"
and injects the ambient tenant. See ADR-007 for the trade-off vs.
a Guid-typed tenant.

## Wiring

```csharp
services.AddKunMultiTenancy(opts =>
{
    opts.UseJwtClaim("tid");              // primary — S2S JWT
    opts.UseHttpHeader("X-Tenant-Id");    // fallback for admin tooling
    opts.FallbackTenantId = "default";    // last resort
});

// In Program.cs pipeline:
app.UseAuthentication();
app.UseKunMultiTenancy();                 // AFTER auth, BEFORE endpoints
app.MapControllers();
```

After the middleware runs, every `ITenantContext.TenantId` /
`AsyncLocalTenantContext.TenantId` read anywhere in the request
scope returns the resolved id.

## For AI Agents

- Do not register a custom `ITenantContext` alongside
  `AddKunMultiTenancy` — the extension already wires
  `AsyncLocalTenantContext` as the singleton. Replacing it defeats
  the middleware.
- Adding a new resolver source (e.g. subdomain, query string)?
  Extend `KunMultiTenancyOptions` with the new entry list and have
  `DefaultTenantResolver` consult it. Keep the API reflection-free.
- The middleware must run **after** the authentication middleware —
  the JWT claims resolver reads `HttpContext.User`, which is only
  populated post-auth. Document this in the consumer's composition
  root.
- Tests use `TestServer` + fake `ClaimsPrincipal` middleware — do
  not mock `ITenantResolver` directly; the resolver's HTTP-context
  inspection is part of the contract.
