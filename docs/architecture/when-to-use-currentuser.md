# When to use `ICurrentUser` vs `IAuthenticatedUser`

**Status**: guidance (not an ADR — implements the direction set in
[ADR-008](./ADR-008-Kun.Identity-module.md) and
[ADR-010](./ADR-010-Kun.Identity-cookie-session.md))
**Last touched**: 2026-04-24 (KN-SELF-005)

Kun Framework exposes two user-context interfaces. They are **not**
competing abstractions — they are layered, with
`IAuthenticatedUser` inheriting `ICurrentUser`. The DI registration
in `Kun.Identity.AspNetCore` binds both to the **same
request-scoped instance**, so the choice of injected interface is
purely about the fields a given caller needs.

This doc exists to answer the next sprint's question before anyone
invents a third adapter.

## Current state

```
Kun.Core.Security.ICurrentUser           ← minimal, S2S JWT-shaped
    │  UserId, Roles, IsAuthenticated
    │
    └── Kun.Identity.Abstractions.IAuthenticatedUser
           ← adds browser / cookie concerns
           │  TenantId, Email, Name, Principal
```

Two runtime bindings ship today:

| Binding | Source | Ships as |
|---------|--------|----------|
| `Kun.Security.JwtCurrentUser` | S2S JWT claims | `ICurrentUser` |
| `Kun.Identity.AspNetCore.KunCurrentUser` | Cookie / OIDC code flow | `ICurrentUser` **and** `IAuthenticatedUser` |

`AnonymousCurrentUser` in `Kun.Core.Security` is the no-auth
sentinel — used by background workers and tests that have no
request context.

## Decision table

| Caller scenario | Inject | Why |
|-----------------|--------|-----|
| gRPC service handler (S2S JWT) | `ICurrentUser` | S2S tokens only carry `sub` + `roles`; browser fields would always be `null`. |
| gRPC interceptor / pipeline behaviour | `ICurrentUser` | Same — operates at the S2S boundary. |
| ASP.NET Core **API** endpoint the browser hits directly | `IAuthenticatedUser` | Cookie middleware populated `TenantId` / `Email` / `Name` / `Principal`; skip the cast. |
| ASP.NET Core **background** worker (`BackgroundService`) | `ICurrentUser` with a **null check** | No request scope exists; bind `AnonymousCurrentUser` explicitly. |
| Outbox / Saga step executor | `ICurrentUser`, usually `AnonymousCurrentUser`. | Background context — inherit the S2S identity only if the invoker explicitly pushed it into OTel baggage (see `Kun.Observability.Enrichment.KunBaggageKeys`). |
| Unit test | `ICurrentUser` with a hand-rolled fake (a `record MyUser(…) : ICurrentUser`) or `AnonymousCurrentUser.Instance`. | Tests depend only on the narrow interface; no HTTP context is needed. |
| Component that **reads tenant** (EF filter, cache key) | `ITenantContext`, **not** either of these. | Tenant identity propagation has its own abstraction — don't couple persistence-layer code to an auth interface. |

## How to extend correctly

Happens rarely. When you need a new field:

1. **Is it present on S2S JWT claims?** → add to `ICurrentUser` and
   to every implementation. Update
   `docs/architecture/ADR-008-Kun.Identity-module.md` with the
   rationale.
2. **Is it browser / cookie only?** → add to `IAuthenticatedUser`.
   Update `docs/architecture/ADR-010-Kun.Identity-cookie-session.md`.
3. **Is it a concern of a different subsystem?** (tracing, tenancy,
   feature flags) → add it to the subsystem's own abstraction
   (`ITenantContext`, `ITracedCurrentUser`, …) and have the
   current-user binding expose or forward it there if needed.
   Example precedent: `ITracedCurrentUser` carries `TraceId` without
   polluting `ICurrentUser`.

## Anti-patterns (do **not** do these)

- ❌ **`ICurrentUserEx` / `IEnhancedCurrentUser` / `ICurrentUserV2`** —
  every one of these reinvents `IAuthenticatedUser`. If
  `IAuthenticatedUser` is missing a field, **add the field to
  `IAuthenticatedUser`** after reading ADR-010, don't fork.
- ❌ **A per-service `ICurrentUser` subinterface in
  `neuroagent-csharp` (or any downstream consumer)** — consumer
  services get no value from owning the abstraction.
- ❌ **`ClaimsPrincipal` injection as a first-class service** —
  always go through `IAuthenticatedUser.Principal`. Direct
  `ClaimsPrincipal` injection hides which host provided it.
- ❌ **Mutable `ICurrentUser` implementations** — instances are
  scoped to a single request; mutating one breaks baggage
  assumptions for the async context.
- ❌ **Making `ICurrentUser` depend on
  `Microsoft.AspNetCore.Http.HttpContext`** — `Kun.Core` is BCL-only
  and the interface stays there so non-HTTP hosts (gRPC-only
  workers, integration tests) can target it without ASP.NET Core
  in their closure.

## If you think you need something new

1. Read this doc end-to-end. Yes, including the anti-patterns.
2. Read ADR-008 and ADR-010.
3. Post the proposed field + its source (JWT claim / cookie /
   header / derived) in the PR description and call out which
   interface it belongs to per the decision rules above.
4. If none of the rules fit, write an ADR that supersedes / extends
   ADR-010 with the new concern — **before** adding code.

If the addition is still ambiguous after all that, leave the code
as-is and raise it at the next framework review. The cost of one
extra sprint of "injecting `IAuthenticatedUser` and casting to
something" is far lower than the cost of shipping a third
sibling abstraction that every downstream service then has to
track.
