# ADR-007: Introduce `Kun.MultiTenancy` and `Kun.MultiTenancy.EntityFrameworkCore` Modules

**Status**: Accepted
**Date**: 2026-04-22
**Author**: claude-kun (Kun framework specialist)
**Context**: The `ITenantContext` abstraction has lived in `Kun.Core` since day one, and `Kun.EntityFrameworkCore.KunDbContext` auto-applies a tenant global query filter in `OnModelCreating`. But the pieces that turn a JWT `tid` claim or `X-Tenant-Id` header into an ambient `ITenantContext` have so far been re-implemented in each consumer service — `neuroagent-csharp/ServiceDefaults` resolves from claims, `Kun.Security.JwtTenantContext` resolves per-request on HTTP, and Edge / Platform / Storage each register their own `AddHttpContextAccessor` + manual claim inspection. Similarly, the SaveChanges-time auto-fill that injects the current tenant on insert was not shipped with the framework; each consumer either forgets it or reimplements it.

---

## Decision

Introduce **two** new framework modules, mirroring the
`Kun.Audit` + `Kun.Audit.EntityFrameworkCore` split:

| Module | Layer | `IsAotCandidate` | Responsibility |
|--------|------:|:----------------:|----------------|
| `Kun.MultiTenancy` | Abstraction / Host-adjacent | `true` | `ITenantResolver`, `DefaultTenantResolver` (JWT / header / fallback), `AsyncLocalTenantContext`, `TenantContextMiddleware`, `AddKunMultiTenancy` / `UseKunMultiTenancy` DI |
| `Kun.MultiTenancy.EntityFrameworkCore` | Infrastructure | `false` | `ApplyKunMultiTenantFilters` ModelBuilder extension, `KunTenantSaveChangesInterceptor` auto-fill, `IKunMultiTenantDbContext` + `KunMultiTenantModelCacheKeyFactory` |

The original handoff brief specified a **single** `Kun.MultiTenancy`
module with `IsAotCandidate=true`. That is incompatible with the
framework's layering rules: EF Core 10 is not AOT-strict (the
pre-existing `Kun.EntityFrameworkCore` runs trim-safe only), so
bundling the ModelBuilder extension + SaveChanges interceptor into an
AOT-candidate module would fail the CI AOT gate. Per the Stop-gate
rule (CLAUDE.md §Forbidden: *do not silently flip IsAotCandidate*),
the split is recorded here rather than bypassed.

---

## Why the tenant filter lives in the framework, not the business layer

The brief's last bullet asks specifically for this — it is the core
design question and worth calling out:

1. **Tenant isolation is a safety property, not a feature.** A
   query that accidentally escapes the filter is an incident. The
   only way to stop it at a uniform standard is to apply the filter
   globally at the framework's ORM seam, not ask every consumer to
   remember.
2. **The filter expression is mechanical.** It is always
   `e.TenantId == tenantContext.TenantId`; there is no per-module
   variation that needs consumer code. Variation should live in the
   tenant *resolver* (where there genuinely is variation: claim
   name, header name, fallback), not in the filter.
3. **The model-cache-key factory is non-obvious and load-bearing.**
   Without a per-tenant cache key, EF Core folds the filter
   constant on first model build and silently leaks data across
   tenants from that point on. Shipping
   `KunMultiTenantModelCacheKeyFactory` with the framework closes
   this hole for every consumer.
4. **The interceptor is mechanical.** Auto-filling `TenantId` on
   insert is a single pattern repeated everywhere; leaving it to
   business code means rows get written with empty tenant ids and
   become invisible to subsequent reads under the filter.

The consumer stays in control of the *policy* — which JWT claim
name, which header, whether to use a "default" fallback, whether to
apply the filter at all. The *mechanism* is owned by the framework.

---

## Module Shape

```csharp
services.AddKunMultiTenancy(opts =>
{
    opts.UseJwtClaim("tid");                 // primary — S2S JWT
    opts.UseHttpHeader("X-Tenant-Id");       // fallback for admin tooling
    opts.FallbackTenantId = "default";       // last resort
});

services.AddDbContext<AppDbContext>((sp, opts) =>
{
    opts.UseNpgsql(connectionString)
        .AddInterceptors(sp.GetRequiredService<KunTenantSaveChangesInterceptor>())
        .ReplaceService<IModelCacheKeyFactory, KunMultiTenantModelCacheKeyFactory>();
});
services.AddKunMultiTenancyEntityFrameworkCore();

// In Program.cs:
app.UseAuthentication();
app.UseKunMultiTenancy();                    // AFTER auth, BEFORE endpoints
app.MapControllers();

// In AppDbContext:
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    base.OnModelCreating(modelBuilder);
    modelBuilder.ApplyKunMultiTenantFilters(_tenantContext);
}
```

`Kun.EntityFrameworkCore.KunDbContext` already applies the filter
automatically and already exposes `TenantIdForModelCacheKey`; consumers
who inherit from it only need the `AddKunMultiTenancy` side and the
interceptor registration.

---

## Alternatives Considered

| Alternative | Why Rejected |
|-------------|--------------|
| A. Single `Kun.MultiTenancy` module with `IsAotCandidate=true` (per brief text) | EF Core inside an AOT-strict module fails the CI gate. Stop-gate rule applies — opens this ADR instead. |
| B. Single module with `IsAotCandidate=false` | Forfeits AOT posture for consumers that only need the resolver + middleware (background workers, AOT-strict hosts without a DB). |
| C. Leave middleware + interceptor to consumers | That is the status quo being replaced. The SaveChanges auto-fill and the model-cache-key factory are load-bearing correctness features that every consumer would otherwise re-implement — or forget. |
| D. **Split into Abstraction + Infrastructure (selected)** | Matches the established `Kun.Audit` / `Kun.Audit.EntityFrameworkCore` pattern. AOT-strict consumers retain the resolver + middleware; EF consumers pick up the filter + interceptor in a sibling module. |

---

## Canonical tenant id type

`Kun.Core.MultiTenancy.ITenantContext.TenantId` has been `string?`
since day one, and `IMultiTenant.TenantId` is `string?`. The brief
mentions `AsyncLocal<Guid?>` and `entity.TenantId == Guid.Empty`; this
module keeps the string shape to preserve coherence with existing
types. The interceptor treats null or whitespace as "not yet
assigned" — a Guid-typed tenant would need to coerce through
`Guid.TryParse` at the resolver boundary anyway, and empty-GUID vs.
empty-string both read as "unset" to downstream code.

---

## Consequences

### Positive

- `neuroagent-csharp/ServiceDefaults` sheds its hand-rolled claim
  resolution, `AddHttpContextAccessor` registration, and the
  per-service `AsyncLocal` plumbing.
- `KunMultiTenantModelCacheKeyFactory` prevents a silent data-leak
  hazard that every consumer would otherwise re-implement.
- The SaveChanges interceptor guarantees that rows never land with
  `null` / empty `TenantId` under normal flow — cutting down a whole
  class of support tickets caused by "my data disappeared" reports.

### Negative / Trade-offs

- Two modules instead of one, tracking the Kun.Audit precedent.
  Marginal overhead in release ceremony.
- Consumers with a custom `DbContext` that does not inherit from
  `Kun.EntityFrameworkCore.KunDbContext` must implement the
  `IKunMultiTenantDbContext` interface to opt into per-tenant
  model-cache partitioning. Documented on the interface.
- The fallback tenant id pattern (`"default"`) groups unauthenticated
  traffic under one bucket. Consumers that want anonymous requests to
  fall *out* of multi-tenant-filtered queries should set
  `FallbackTenantId = null` — the filter then evaluates
  `e.TenantId == null`, which returns rows explicitly flagged as
  shared (or nothing at all).

### Risks

| Risk | Mitigation |
|------|------------|
| OTel / EF Core upgrade regresses AOT on the main module | CI AOT matrix includes `Kun.MultiTenancy`; stop-gate rule applies. |
| Middleware runs before `UseAuthentication()` in a consumer's pipeline, resolver reads an empty `ClaimsPrincipal` | Documented in AGENTS.md; a future follow-up could add a startup-time validation that flags the ordering. |
| Consumer forgets `ReplaceService<IModelCacheKeyFactory, KunMultiTenantModelCacheKeyFactory>` | Filter silently folds on first tenant and leaks. Documented; a future follow-up could add a `UseKunMultiTenancy()` extension on `DbContextOptionsBuilder` that wires both the factory and the interceptor in one call. |

---

## Follow-ups

1. **Migrate `neuroagent-csharp/ServiceDefaults`** off its hand-rolled
   tenant-resolution helper onto `AddKunMultiTenancy`.
2. **`UseKunMultiTenancy()` on `DbContextOptionsBuilder`** — a single
   extension that wires both the interceptor and the
   model-cache-key factory, reducing consumer ceremony.
3. **Startup ordering validation** — an `IStartupFilter` that flags
   when `UseKunMultiTenancy` runs before `UseAuthentication`.
4. **Subdomain / query-string resolver sources** — extend
   `KunMultiTenancyOptions` when a consumer requires them.

---

## References

- Handoff: task brief P1-KN-MultiTenancy
- `Kun.Core.MultiTenancy.ITenantContext` / `Kun.Core.Domain.IMultiTenant` — the pre-existing contracts
- `Kun.EntityFrameworkCore.KunDbContext` — the filter auto-application this module generalises
- `Kun.IntegrationTests/MultiTenancy/TenantFilterTests.cs` — the per-tenant `IModelCacheKeyFactory` pattern this module ships framework-wide
- Related ADR: ADR-003 (Kun.Audit module split for the same AOT / EF Core reason)
