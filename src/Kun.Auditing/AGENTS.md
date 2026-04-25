<!-- Parent: ../AGENTS.md -->
<!-- Generated: 2026-04-22 | Updated: 2026-04-22 -->

# Kun.Auditing

## Purpose

EF-Core-interceptor-based audit trail for mutations of designated entity types. Mutations flow through `AuditingSaveChangesInterceptor` → `IAuditSink` → `IOutboxWriter`, so audit rows commit atomically with the business change. The emitted integration event is `AuditEventIntegrationEvent` (stable id `kun.audit.event.v1`, 13-field payload per Gap-closure plan M4).

## Key Files

| File | Description |
|------|-------------|
| `Kun.Auditing.csproj` | `IsAotCandidate=true`, depends on `Kun.Core`, `Kun.EventBus`, `Kun.EntityFrameworkCore` |
| `AuditableAttribute.cs` | Documentation-only marker (see below) |
| `IAuditableEntityRegistry.cs` / `AuditableEntityRegistry.cs` | `FrozenSet<Type>`-backed O(1) membership lookup |
| `AuditingSaveChangesInterceptor.cs` | EF `ISaveChangesInterceptor`; walks `ChangeTracker.Entries()` and emits one event per matching change |
| `OutboxAuditSink.cs` | Default `IAuditSink` — forwards to `IOutboxWriter` for atomic commit |
| `AuditContext.cs` / `Middleware/AuditContextMiddleware.cs` | Scoped DTO + ASP.NET middleware that captures IP/UA/trace id per request |
| `Events/AuditEventIntegrationEvent.cs` | Integration event record (stable id `kun.audit.event.v1`) |
| `AuditJsonContext.cs` | Source-generated `JsonSerializerContext` for AOT-safe serialization |
| `DependencyInjection/AuditingServiceCollectionExtensions.cs` | `AddKunAuditing()`, `AddAuditable<T>()`, `RegisterAuditing()` (on `IEventTypeRegistrar`) |

## For AI Agents

### Working In This Directory

- **`[Auditable]` is docs-only.** The attribute exists so that code reviewers can see intent on the entity class, but the runtime never reads it. `AuditingSaveChangesInterceptor` consults `IAuditableEntityRegistry.RegisteredTypes` (a `FrozenSet<Type>`) populated by `services.AddAuditable<TEntity>()` calls at startup.
- **Why path (c) (plan A3)**: path (a) `IsAotCandidate=false` would hide auditing from AOT-target consumers; path (b) source generator is over-engineered at the current entity count. Explicit DI registration matches the `Kun.Mediator` handler-registration pattern and keeps the module AOT-publish-clean with **zero** trim / AOT warnings.
- **Forbidden here**: `Type.GetCustomAttributes`, `Assembly.GetTypes`, `Activator.CreateInstance`, parameterless `JsonSerializer.Serialize(obj)`. Any new audit-side feature that needs reflection must route through a source generator or be pushed into a non-AOT module.
- **Actor identity is pushed, not pulled.** `AuditContext.ActorId` / `ActorType` defaults to `"system"`. Consumers that want the JWT subject must populate `ActorId` from their own `ICurrentUser` bridge — the auditing module intentionally does not depend on `Kun.Security`.
- **Stable event id**: `kun.audit.event.v1`. Bump to `.v2` (new record type) whenever the 13-field payload shape changes; do not mutate v1 in place.
- **Atomicity contract**: the default `OutboxAuditSink` forwards to `IOutboxWriter` so the audit row lives in the same transaction as the business write. Replacements must preserve this — a direct-write sink that logs to an external store outside the ambient transaction violates the audit reliability guarantee.

### Testing Requirements

- Unit tests (`tests/Kun.Auditing.Tests`): in-memory EF provider via `Microsoft.EntityFrameworkCore.InMemory`? The module's tests use only `NSubstitute` + vanilla `EntityEntry` mocking plus a tiny in-memory sink — no DB required.
- Integration tests (`tests/Kun.IntegrationTests/Auditing`): Testcontainers Postgres; skip gracefully when Docker is unavailable.
- AOT publish: `dotnet publish src/Kun.Auditing -c Release -p:PublishAot=true` must be **warning-free**. The analyzer pass is the truly portable verification; the linker step may fail locally on Windows without MSVC, but CI (linux-x64) succeeds.

### Common Patterns

```csharp
// Startup
services.AddKunEventBus(b => b
    .RegisterAuditing()              // kun.audit.event.v1
    .Register<OrderPlacedIntegrationEvent>("order.placed.v1"));
services.AddKunAuditing();
services.AddAuditable<Order>();
services.AddAuditable<Customer>();

services.AddDbContext<CatalogDbContext>((sp, opts) =>
    opts.UseNpgsql(cs)
        .UseKunSchema("catalog")
        .AddAuditingInterceptor(sp));

// Pipeline
app.UseMiddleware<AuditContextMiddleware>();
```

## Dependencies

### Internal

- `Kun.Core` — `ITenantContext`.
- `Kun.EventBus` — `IOutboxWriter`, `IEventTypeRegistrar`.
- `Kun.EntityFrameworkCore` — EF interceptor base types, `KunDbContext` integration.

### External

- `Microsoft.EntityFrameworkCore` (for `ISaveChangesInterceptor`, `EntityEntry`).
- `Microsoft.Extensions.DependencyInjection.Abstractions`, `Microsoft.Extensions.Logging.Abstractions`.
- `Microsoft.AspNetCore.App` framework reference (for `HttpContext`, `IMiddleware`).

<!-- MANUAL: -->
