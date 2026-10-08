<!-- Parent: ../AGENTS.md -->
<!-- Generated: 2026-04-22 -->

# Kun.MultiTenancy.EntityFrameworkCore

## Purpose

EF Core integration for `Kun.MultiTenancy`. Ships two things:

- `ApplyKunMultiTenantFilters(ModelBuilder, ITenantContext)` — a
  ModelBuilder extension that walks the model, finds every entity
  implementing `Kun.Core.Domain.IMultiTenant`, and attaches a
  `HasQueryFilter(e => e.TenantId == ctx.TenantId)` predicate so
  cross-tenant reads are impossible through the EF pipeline.
- `KunTenantSaveChangesInterceptor` — a `SaveChangesInterceptor`
  that walks `ChangeTracker.Entries<IMultiTenant>()` for added rows
  and, when the entity's `TenantId` is null or empty, injects the
  ambient `ITenantContext.TenantId`.

## Layer and AOT

- Layer: **Infrastructure** — depends on `Kun.MultiTenancy` +
  `Microsoft.EntityFrameworkCore`.
- `<IsAotCandidate>false</IsAotCandidate>` — trim-safe only, matching
  `Kun.EntityFrameworkCore`. Consumers running full native AOT
  cannot use this module; they can still consume `Kun.MultiTenancy`
  directly for the resolver + AsyncLocal context and build their
  own persistence filter layer. See ADR-007.

## Wiring

```csharp
services.AddKunMultiTenancy(opts => opts.UseJwtClaim("tenant_id"));

services.AddDbContext<AppDbContext>((sp, opts) =>
{
    opts.UseNpgsql(connectionString)
        .AddInterceptors(sp.GetRequiredService<KunTenantSaveChangesInterceptor>());
});

services.AddKunMultiTenancyEntityFrameworkCore();   // registers the interceptor

// In your DbContext.OnModelCreating:
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    base.OnModelCreating(modelBuilder);
    modelBuilder.ApplyKunMultiTenantFilters(_tenantContext);
}
```

`KunDbContext` (the framework's base class) already applies the
filter automatically; this module's extension is for consumers who
inherit from a different DbContext base.

## For AI Agents

- The ModelBuilder extension here uses the same
  `IMultiTenant`-via-`Expression.Property` scan that
  `KunDbContext.ApplyGlobalQueryFilters` uses. Keep the two in sync
  — a silent behavioural drift between the two code paths would be
  a tenancy-leak hazard.
- The interceptor only touches `Added` entities. Update /
  soft-delete tenancy is already enforced by the global query
  filter; overwriting `TenantId` on update would mask tenant-drift
  bugs.
- Do not introduce runtime reflection here. The `Expression` APIs
  used to compose the filter predicate are compile-time metadata;
  the interceptor relies on the `IMultiTenant` interface cast.

生产装配使用 `options.Production = true`；请求头与默认租户仅供显式开发兼容模式使用。后台通过 `ITenantContext.BeginScope` 回灌，详情见 `docs/security/multi-tenancy-rls.md`。
