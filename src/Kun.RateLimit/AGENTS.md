<!-- Parent: ../AGENTS.md -->
<!-- Generated: 2026-04-22 -->

# Kun.RateLimit

## Purpose

Unified rate-limiting for any ASP.NET Core service in the Kun
ecosystem. Replaces the hand-rolled `AddRateLimiter(...)` +
`AddPolicy("api", ...)` + `AddPolicy("auth", ...)` boilerplate that
neuroagent-csharp `ServiceDefaults.AddNeuroAgentRateLimiter` was
carrying.

Single entry point:

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
        // optional log / alert hook
        return ValueTask.CompletedTask;
    };
});
```

Consumer still calls `app.UseRateLimiter()` in the middleware
pipeline and attaches policies via `.RequireRateLimiting("api")`.

## Layer and AOT

- Layer: **Infrastructure / Host-adjacent**. Depends on the
  `Microsoft.AspNetCore.App` shared framework for
  `Microsoft.AspNetCore.RateLimiting`.
- `<IsAotCandidate>true</IsAotCandidate>`. The implementation
  deliberately uses the reflection-free partitioner-lambda overload
  of `AddPolicy` so `AddKunRateLimit` stays AOT-analyzer clean. If an
  ASP.NET Core upgrade regresses the analyzer, **stop-gate the upgrade**
  rather than flipping the module to `false` silently.

## Design Notes

| Concern | Decision |
|---------|----------|
| Limiter algorithm | Fixed window. Simple to reason about, matches neuroagent's existing semantics. Extension to sliding/token-bucket is a follow-up. |
| Partition key | Enum with three values (`Tenant`, `User`, `Ip`) resolved internally from `ClaimsPrincipal`'s `tenant_id` / `sub` / `NameIdentifier` claims or `RemoteIpAddress` / `X-Forwarded-For` respectively. Falls back to `"anonymous"` when the claim is missing. |
| `Retry-After` | Populated from the limiter's `RateLimitMetadata.RetryAfter` when available; otherwise derived from the matched window's `Period`. Consumers always see a hint. |
| `OnRejected` hook | Called **after** the default 429 + `Retry-After` are applied. The hook can log / alert / emit a metric without having to reconstruct the response itself. |

## Partition resolution

| `RateLimitPartitionKey` | Source | Fallback |
|-------------------------|--------|----------|
| `Tenant` | `ClaimsPrincipal.FindFirst("tenant_id").Value` | `"anonymous"` |
| `User` | `ClaimsPrincipal.FindFirst("sub").Value` ?? `ClaimsPrincipal.Identity.Name` | `"anonymous"` |
| `Ip` | First segment of `X-Forwarded-For`, then `Connection.RemoteIpAddress`, then `"unknown"` | `"unknown"` |

## For AI Agents

- Do **not** copy the Gateway-specific `IRateLimitPartitionResolver`
  pattern from `Kun.Gateway`. Gateway's resolvers target YARP
  transforms; this module targets ASP.NET Core endpoint rate limiters
  and owns its own closed-set enum to keep the API AOT-friendly.
- Adding a new algorithm (sliding / token-bucket / concurrency)?
  Extend `WindowRule` with an `Algorithm` enum and branch in the
  partitioner lambda. Keep the public surface backward-compatible.
- Keep the partitioner lambda reflection-free — no
  `Activator.CreateInstance`, no `Type.GetMethod`.
- Tests in `tests/Kun.RateLimit.Tests` stand up a real
  `WebApplication` via `TestServer`; do not mock ASP.NET Core
  primitives — the rate-limit middleware's ordering is part of the
  contract.
