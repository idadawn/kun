# Schema-per-service with `KunDbContext`

> **Scope**: how to run multiple Kun-based services against a single PostgreSQL
> instance by giving each service its own schema (`catalog`, `order`, …), with
> business tables, the Outbox, Saga tables, and the EF migrations-history table
> all co-located under that schema.
>
> Applies to: any `KunDbContext`-derived DbContext using `Npgsql.EntityFrameworkCore.PostgreSQL`.
>
> Last updated: 2026-04-22.

## Why schema-per-service

Running many services against one physical Postgres instance is operationally
cheap (one cluster to patch, one backup path, one connection pool). But putting
everything in `public` makes name collisions inevitable — two services both want
an `outbox_messages`, an `__ef_migrations_history`, and so on.

Kun's answer: each service overrides `KunDbContext.DefaultSchema` with its own
name (`"catalog"`, `"order"`, etc.), and `UseKunSchema(schema)` on the options
builder wires the migrations-history table into the same schema. A
`SchemaConsistencyInterceptor` then fails fast at the first `SaveChangesAsync`
if the two ever disagree, so misconfigurations surface in dev instead of
silently leaking rows across schemas in production.

## End-to-end recipe

### 1. Override `DefaultSchema` on your DbContext

```csharp
using Kun.Core.MultiTenancy;
using Kun.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

public sealed class CatalogDbContext : KunDbContext
{
    public DbSet<Product> Products => Set<Product>();

    public CatalogDbContext(
        DbContextOptions<CatalogDbContext> options,
        ITenantContext tenantContext)
        : base(options, tenantContext)
    {
    }

    /// <inheritdoc/>
    protected override string? DefaultSchema => "catalog";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder); // applies HasDefaultSchema(DefaultSchema)
        modelBuilder.ApplyConfiguration(new ProductConfiguration());
    }
}
```

`KunDbContext.OnModelCreating` calls `modelBuilder.HasDefaultSchema(DefaultSchema)`
before applying the Outbox and tenant-filter configurations, so every
unqualified `.ToTable("x")` lands under `catalog`.

### 2. Chain `UseKunSchema("catalog")` in the design-time factory and DI

The design-time factory drives `dotnet ef migrations add`:

```csharp
using Kun.Core.MultiTenancy;
using Kun.EntityFrameworkCore.Extensions; // UseKunSchema lives here
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

internal sealed class CatalogDesignTimeFactory
    : IDesignTimeDbContextFactory<CatalogDbContext>
{
    public CatalogDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseNpgsql("Host=localhost;Database=catalog_design;Username=postgres;Password=postgres")
            .UseKunSchema("catalog")   // wires MigrationsHistoryTableSchema
            .Options;

        return new CatalogDbContext(options, NullTenantContext.Instance);
    }
}
```

Same pattern in the runtime `AddDbContext` call:

```csharp
services.AddDbContext<CatalogDbContext>((_, options) =>
    options
        .UseNpgsql(connectionString)
        .UseKunSchema("catalog"));
```

Order matters: `.UseNpgsql(...)` must precede `.UseKunSchema(...)` so the
relational provider extension already exists by the time `UseKunSchema` mutates
`MigrationsHistoryTableSchema`.

### 3. Regenerate migrations under the new schema

For an existing service already carrying `public`-schema migrations, regenerate
them once:

```bash
# Remove the old public-schema migrations
dotnet ef migrations remove \
  --project samples/Kun.Sample.Catalog \
  --startup-project samples/Kun.Sample.Catalog \
  --force

# Re-add under the new schema — the DesignTimeFactory's UseKunSchema("catalog")
# drives the correct MigrationsHistoryTable schema.
dotnet ef migrations add Initial_SchemaQualified \
  --project samples/Kun.Sample.Catalog \
  --startup-project samples/Kun.Sample.Catalog \
  --output-dir Infrastructure/Migrations
```

Inspect the generated `_Initial_SchemaQualified.cs` — every `CreateTable` /
`CreateIndex` / `DropTable` call now carries `schema: "catalog"`, and the
generated `ModelSnapshot.cs` starts with `.HasDefaultSchema("catalog")`.

#### Windows / Git Bash note

Both of the commands above must run with `MSYS_NO_PATHCONV=1` in Git Bash,
otherwise `--project` values get path-mangled:

```bash
MSYS_NO_PATHCONV=1 dotnet ef migrations add Initial_SchemaQualified \
  --project samples/Kun.Sample.Catalog \
  --startup-project samples/Kun.Sample.Catalog \
  --output-dir Infrastructure/Migrations
```

Run the two services' `dotnet ef migrations add` commands **sequentially**
(not in parallel) — they compete for the same `src/Kun.EntityFrameworkCore`
build artefacts.

### 4. Create the schemas in Postgres

`dotnet ef database update` will call `EnsureSchema("catalog")` via the
generated migration, so in most cases you do nothing. For a plain
`EnsureCreatedAsync()` flow (tests / quick prototyping), EF also issues
`CREATE SCHEMA IF NOT EXISTS catalog`. You can create the schemas ahead of
time if you want the service accounts to have restricted grants:

```sql
CREATE SCHEMA IF NOT EXISTS catalog;
CREATE SCHEMA IF NOT EXISTS "order";  -- note: 'order' is reserved, quote it
GRANT USAGE ON SCHEMA catalog TO svc_catalog;
GRANT USAGE ON SCHEMA "order" TO svc_order;
```

### 5. Verify with `dotnet ef migrations list`

After migrations regenerate, confirm the history table lives in the right
schema:

```bash
dotnet ef migrations list --project samples/Kun.Sample.Catalog
# shows catalog.__ef_migrations_history
dotnet ef migrations list --project samples/Kun.Sample.Order
# shows order.__ef_migrations_history
```

## The safety net: `SchemaConsistencyInterceptor`

`KunDbContext.OnConfiguring` unconditionally registers a
`SchemaConsistencyInterceptor` whose job is exactly one thing: compare
`Model.GetDefaultSchema()` against the relational provider's
`MigrationsHistoryTableSchema` at the first `SaveChangesAsync`, and throw
`InvalidOperationException` with a remediation hint when they diverge.

This catches the most common error — overriding `DefaultSchema` on your
DbContext but forgetting to call `UseKunSchema(...)` on the options builder.
Without the interceptor, business tables would land under `catalog` while
`__ef_migrations_history` silently stays in `public`, a migration-drift
timebomb. With the interceptor, the first save fails loudly:

```
InvalidOperationException: KunDbContext.DefaultSchema='catalog' does not match
MigrationsHistoryTable schema='(null)'. Call .UseKunSchema("catalog") on
DbContextOptionsBuilder to wire both consistently.
```

The check is gated by `Interlocked.CompareExchange` so it runs exactly once
per DbContext instance — zero overhead after the first transaction.

## Outbox and Saga co-location

Because the `KunDbContext` base class applies `HasDefaultSchema(DefaultSchema)`
**before** `ApplyConfiguration(new OutboxMessageConfiguration())`, the Outbox
table inherits the same schema without any extra configuration. Similarly,
Saga tables registered via `modelBuilder.ApplyKunSagaConfiguration()` land in
the service schema.

This is deliberate — having `catalog.outbox_messages` in the same schema as
`catalog.catalog_products` lets a single transaction atomically commit both,
which is the entire point of the Outbox pattern. Putting the outbox in a
shared `outbox` schema would break that atomicity story (cross-schema writes
still commit atomically, but the operational mental model gets muddier, and
separate grants / backup policies become harder to reason about).

## Running multiple services against one PostgreSQL

Once each service has its own schema, they can target the same physical
Postgres instance and the same logical database. Aspire wires this up
trivially — the `Kun.Sample.AppHost` sample points `Catalog` and `Order` at
the same `postgres` resource:

```csharp
var postgres = builder.AddPostgres("postgres");
var kun = postgres.AddDatabase("kun");

builder.AddProject<Projects.Kun_Sample_Catalog>("catalog")
    .WithReference(kun);
builder.AddProject<Projects.Kun_Sample_Order>("order")
    .WithReference(kun);
```

With schema-per-service in place, the two services coexist without name
collisions — each `SELECT * FROM catalog.outbox_messages` stays well-scoped.

## Reference implementation

- `src/Kun.EntityFrameworkCore/KunDbContext.cs` — `DefaultSchema` virtual
  property + auto-registration of the interceptor.
- `src/Kun.EntityFrameworkCore/Extensions/KunDbContextOptionsBuilderExtensions.cs` —
  the `UseKunSchema` extension.
- `src/Kun.EntityFrameworkCore/Interceptors/SchemaConsistencyInterceptor.cs` —
  the fail-fast guard.
- `samples/Kun.Sample.Catalog/Infrastructure/CatalogDbContext.cs` + migrations —
  concrete example with a `catalog` schema.
- `samples/Kun.Sample.Order/Infrastructure/OrderDbContext.cs` + migrations —
  concrete example with an `order` schema (plus Saga tables).
- `tests/Kun.IntegrationTests/Persistence/SchemaPerServiceTests.cs` —
  Testcontainers-backed test that boots two `KunDbContext` subclasses under
  distinct schemas against one Postgres and asserts physical isolation in
  `information_schema.tables`.
