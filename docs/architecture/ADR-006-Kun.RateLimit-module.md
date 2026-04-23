# ADR-006: Introduce `Kun.RateLimit` Module

**Status**: Accepted
**Date**: 2026-04-22
**Author**: claude-kun (Kun framework specialist)
**Context**: `neuroagent-csharp/src/NeuroAgent.ServiceDefaults/Extensions.cs` has been carrying an `AddNeuroAgentRateLimiter` helper that hand-wires `AddRateLimiter(...)` + `AddPolicy("api", ...)` + `AddPolicy("auth", ...)` with tenant / ip partitioning for each consumer service (P1-CS-RATELIMIT). `Kun.Gateway` ships its own YARP-oriented `AddKunRateLimiter` but that extension is hard-coded to a single `kun-default` policy with 100 req/min and is tightly coupled to the Gateway's partition resolver abstraction. Neither pattern serves non-gateway ASP.NET Core services that need named windows with different partitioning per window.

---

## Decision

Introduce a new framework module `Kun.RateLimit` with a single entry
point:

```csharp
services.AddKunRateLimit(opts =>
{
    opts.AddWindow("api", w =>
    {
        w.PermitLimit = 60;
        w.Period = TimeSpan.FromMinutes(1);
        w.PartitionKey = RateLimitPartitionKey.Tenant;
    });
    opts.AddWindow("auth", w =>
    {
        w.PermitLimit = 10;
        w.Period = TimeSpan.FromMinutes(1);
        w.PartitionKey = RateLimitPartitionKey.Ip;
    });
    opts.OnRejected = (ctx, ct) =>
    {
        // optional log / alert hook — response already committed
        return ValueTask.CompletedTask;
    };
});
```

The module owns:

- `KunRateLimitOptions` — root options with a `Dictionary<string, WindowRule>`
  of named windows and an optional `OnRejected` hook.
- `WindowRule` — per-window: `Period`, `PermitLimit`, `QueueLimit`,
  `AutoReplenishment`, and `PartitionKey` (enum).
- `RateLimitPartitionKey` — closed-set enum: `Tenant` | `User` | `Ip`.
  Keeps the API surface AOT-friendly (no open-set resolver interface
  to register per-partition-source).
- `PartitionKeyResolver` (internal) — maps enum + `HttpContext` to
  partition string, with consistent fallbacks (`"anonymous"` /
  `"unknown"`).
- `AddKunRateLimit` extension — translates the options into
  `AddRateLimiter(...)` + one `AddPolicy(name, partitioner-lambda)`
  call per window. Sets a global `OnRejected` that writes 429 +
  `Retry-After` and invokes the consumer hook.

---

## Module Shape and AOT

| Property | Value |
|----------|-------|
| Layer | **Infrastructure / Host-adjacent** |
| `FrameworkReference` | `Microsoft.AspNetCore.App` (for `Microsoft.AspNetCore.RateLimiting` + `System.Threading.RateLimiting`) |
| `<IsAotCandidate>` | `true` — verified locally via `dotnet publish -p:PublishAot=true /warnaserror` up to the native-link stage. Zero analyzer warnings |
| Reflection | None. The `AddPolicy` overload used accepts a `Func<HttpContext, RateLimitPartition<string>>` lambda; `AddPolicy<TPolicy>` (DI-activated) is deliberately avoided |

---

## Alternatives Considered

| Alternative | Why Rejected |
|-------------|--------------|
| A. Extend `Kun.Gateway.AddKunRateLimiter` | Gateway's extension is hard-coded to one policy and one `IRateLimitPartitionResolver` registration. Generalising it would bloat a host-layer module with concerns that belong in an Abstraction/Infrastructure module. |
| B. Reuse `Kun.Gateway.IRateLimitPartitionResolver` in a shared module | The resolver interface is open-set; every consumer that adds a new partition source (e.g. "API key") has to register a resolver impl. That's the wrong tradeoff for 95% of consumers who only want tenant / user / ip. The enum in this module captures the common cases directly. |
| C. Leave it in `ServiceDefaults` per consumer | That is the status quo being replaced; it cost neuroagent-csharp a redundant helper in every service with hard-coded limits. |
| D. **New `Kun.RateLimit` module (selected)** | Clean separation from the Gateway (which stays a YARP-specific host module); single-call registration for any ASP.NET Core service; closed-set partition enum keeps the API AOT-strict. |

---

## Design Notes

- **Fixed window**, not sliding / token-bucket / concurrency. Matches
  neuroagent-csharp's existing behaviour and what most HTTP APIs
  actually want. Extending `WindowRule` with an `Algorithm` enum is a
  follow-up once there is a concrete need.
- **`Retry-After` header always present on 429**. The middleware
  reads the lease's `MetadataName.RetryAfter` when available;
  otherwise it falls back to the largest registered window's
  `Period`. The caller always sees a usable hint.
- **`OnRejected` hook runs after the response is committed**. Design
  intent is logging / metrics / alerting, not response mutation.
  Documented on the hook signature.
- **Partition enum**, not open-set interface. Gateway keeps its own
  `IRateLimitPartitionResolver` for per-route transforms; this module
  keeps the ASP.NET Core endpoint surface clean.

---

## Consequences

### Positive

- neuroagent-csharp's `AddNeuroAgentRateLimiter` can be replaced by a
  three-line `AddKunRateLimit(opts => ...)`.
- Framework-level ergonomics: named windows match the `.RequireRateLimiting("api")`
  decoration that ASP.NET Core already expects.
- AOT-strict — another framework module that consumer hosts can pull
  in without losing their AOT posture.

### Negative / Trade-offs

- Two rate-limit extensions in the framework now (`Kun.Gateway.AddKunRateLimiter`
  for YARP, `Kun.RateLimit.AddKunRateLimit` for services). The
  `Kun.Gateway` version is the narrower tool; it stays because YARP's
  transform pipeline is different enough from endpoint rate limiting
  that one size doesn't fit both.
- Closed-set partition enum: consumers that need API-key partitioning
  cannot plug one in without extending the enum in the framework.
  This is a deliberate choice; revisit when a consumer requires it.

### Risks

| Risk | Mitigation |
|------|------------|
| ASP.NET Core `RateLimiting` upgrade regresses AOT | CI AOT matrix includes `Kun.RateLimit`; stop-gate rule applies. |
| Consumer forgets `app.UseRateLimiter()` in the pipeline | Middleware is the consumer's responsibility — documented in `AGENTS.md`. Adding a `UseKunRateLimit` shim is a future-work item. |

---

## Follow-ups

1. **Migrate `neuroagent-csharp/ServiceDefaults`** off
   `AddNeuroAgentRateLimiter` onto `AddKunRateLimit`.
2. **Sliding / token-bucket variants**: extend `WindowRule` with an
   `Algorithm` enum when a consumer requires it; today's single
   algorithm keeps the surface narrow.
3. **`UseKunRateLimit` middleware shim** for consumers that want to
   call `app.UseKunRateLimit()` instead of `UseRateLimiter()`.
4. **Reconcile with `Kun.Gateway.AddKunRateLimiter`**: once a real
   consumer needs YARP + endpoint rate limits together, decide
   whether to merge or keep them split.

---

## References

- Handoff: task brief P1-KN-RateLimit
- `neuroagent-csharp/src/NeuroAgent.ServiceDefaults/Extensions.cs` —
  `AddNeuroAgentRateLimiter` being abstracted
- `src/Kun.Gateway/RateLimiting/KunRateLimiterExtensions.cs` — the
  sibling YARP-specific extension that stays in place
