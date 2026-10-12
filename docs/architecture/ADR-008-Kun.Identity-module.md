# ADR-008: Introduce `Kun.Identity` Module — Reversing ADR-001

> Implementation update (S1-KUN-004): the [identity client guide](../guides/identity-client.md) and [canonical S2S guide](../security/s2s-jwt.md) supersede the legacy issuer and incomplete cookie-flow examples below. The original rationale is retained as history.

**Status**: Accepted
**Date**: 2026-04-22
**Author**: claude-kun (Kun framework specialist)
**Supersedes (partially)**: [ADR-001](./ADR-001-Kun.Identity-module-decision.md) — *Do Not Introduce a New `Kun.Identity` Module*
**Context**: ADR-001 (Apr 2026) declined to create a `Kun.Identity` module because Phase 1 identity needs were fully covered by `Kun.Gateway` (OIDC → S2S JWT translation) and `Kun.Security` (S2S JWT validation + JWKS). That decision explicitly anticipated a revisit: *"If future phases need cross-cutting identity abstractions (e.g., `IPermissionChecker` with policy store), we will revisit this ADR."* Consumer-side divergence observed across three neuroagent-csharp services has now materialised exactly that signal.

---

## What changed since ADR-001

Three concrete divergences drove the revisit:

1. **Three near-duplicate `AddJwtBearer` configurations.** `neuroagent-csharp` Edge, Platform, and Notification services each hand-roll `services.AddAuthentication().AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, opts => ...)`. The issuer / audience / JWKS URL pointing at Authentik is copied verbatim; the clock-skew and expiry-required flags differ slightly; each service reaches a subtly different security posture. The only way to pull these back into a single policy is a framework abstraction.
2. **Per-service `S2SJwtIssuer` wiring.** The existing `Kun.Security.S2SJwt.S2SJwtIssuer` exists as an implementation, but the public DI surface is verbose (`services.AddOptions<S2SJwtOptions>()...` + manual key loading). Consumers have been wrapping it with small adapter helpers — five different helpers, five different signatures.
3. **OIDC code-exchange lives nowhere.** Authentik supports both client-credentials (used by the gateway) and authorization-code flows. The code-flow exchange is not covered by either `Kun.Gateway` or `Kun.Security` today, so Edge open-coded an `HttpClient.PostAsync` against `/application/o/token/` with inline form encoding. That code has drifted into five subtle variants across services.

The generalisable pattern — validate external tokens, mint S2S tokens, talk to an OIDC provider — is orthogonal to the gateway / security split. Lifting it to `Kun.Identity` consolidates all three configurations behind one fluent builder.

---

## Decision

Introduce `src/Kun.Identity/` (IsAotCandidate=true) exposing three
public abstractions and a single DI entry:

```csharp
services.AddKunIdentity(id =>
{
    id.AddJwtBearer(opts => { opts.Issuer = ...; opts.Audience = ...; opts.JwksUri = ...; });
    id.AddS2SIssuer(opts => { opts.Issuer = ...; opts.SigningKeyPem = ...; });
    id.AddOidcClient("authentik", opts => { opts.Authority = ...; opts.ClientId = ...; opts.ClientSecret = ...; });
});
```

| Abstraction | Purpose | Backing implementation |
|-------------|---------|-------------------------|
| `IJwtValidator` | Validate external Bearer JWT → `ClaimsPrincipal?` | `KunJwtValidator` (`JsonWebTokenHandler` + `ConfigurationManager<OpenIdConnectConfiguration>` JWKS rotation) |
| `IJwtIssuer` | Mint signed JWTs (S2S flow) | `KunJwtIssuer` (RS256 via `JsonWebTokenHandler` + `RsaSecurityKey`) |
| `IOidcClient` | OIDC discovery + `authorization_code` exchange | `KunOidcClient` (`HttpClient` + source-gen `JsonSerializerContext`) |

The module is a **facade**, not an IdP. It does not embed a token
database, does not sign user credentials, does not implement the
OIDC server role. Running Authentik / Keycloak / Auth0 remains the
consumer's responsibility; `Kun.Identity` gives them a uniform
client surface against that infrastructure.

---

## Relationship to existing Kun.Security / Kun.Gateway

- `Kun.Security.S2SJwt` (Issuer, Verifier, Options, JWKS middleware)
  stays in place unchanged for backward compatibility. New services
  prefer the `Kun.Identity.IJwtIssuer` / `IJwtValidator` surface
  because it also covers external tokens and OIDC clients in one
  registration — but existing consumers of `Kun.Security.S2SJwt`
  keep working.
- `Kun.Gateway.S2sTokenIssuer` remains the Gateway-internal S2S
  issuer. A future refactor can have it implement
  `Kun.Identity.IJwtIssuer` and delete the duplicate code path —
  that migration is out of scope for this ADR.
- `Kun.Security.KunServiceAuthenticationExtensions.AddKunServiceAuthentication`
  is the ASP.NET Core `AddJwtBearer` pipeline wiring. It is not
  subsumed here. Consumers that need ASP.NET Core's auth middleware
  still call it; consumers that need framework-neutral bearer
  validation (gRPC interceptors, background workers, custom
  middleware) use `IJwtValidator` directly.

---

## Why ADR-001 was right at the time, and is wrong now

ADR-001 weighed four drivers:

| Driver | Still true today? |
|--------|-------------------|
| "Capability already exists" — Gateway + Security cover the lifecycle | **Partially.** They cover S2S, not external Bearer + OIDC code-flow. |
| "No gap in Phase 1" | **No longer true.** P1-CS-007 / P1-CS-010 shipped, and three consumer services now carry drift that a framework abstraction would eliminate. |
| "Avoid framework bloat" | Still a concern. This module adds ~9 types + two tests to the framework surface. The divergence cost exceeds that now. |
| "Downstream simplicity" | **Inverted.** Downstream is currently *less* simple because each service re-implements the same wiring. |

The drivers that made ADR-001 correct at the time — Phase 1 scope,
no observed divergence, single pattern per service — are no longer
the situation. ADR-008 records the revisit with the exact signal
ADR-001 asked for.

---

## Consequences

### Positive

- Consumer services replace per-repo auth helpers with one
  `AddKunIdentity(...)` call — the divergence driver goes away.
- `IJwtValidator` is framework-neutral; gRPC interceptors /
  background workers can do bearer validation without spinning up
  ASP.NET Core's auth pipeline.
- OIDC code-exchange moves into the framework, closing the last
  hand-rolled `HttpClient.PostAsync` against `/token` in consumer
  code.

### Negative / Trade-offs

- Framework surface grows by one module (~9 public types).
- Two S2S issuer implementations coexist until the
  `Kun.Security` → `Kun.Identity` migration lands. Consumers picking
  "which one" is mildly confusing until the canonical choice is
  `Kun.Identity`.
- The OIDC client only implements authorization-code grant. Refresh,
  client-credentials, and device-flow are follow-ups.

### Risks

| Risk | Mitigation |
|------|------------|
| Microsoft.IdentityModel upgrade regresses AOT | CI AOT matrix includes `Kun.Identity`; stop-gate rule applies. |
| Consumer mistakes `IJwtIssuer` for user-password auth | AGENTS.md is explicit: this module is an identity *client*, not an IdP. |
| Drift between `Kun.Security.S2SJwtIssuer` and `Kun.Identity.KunJwtIssuer` during the transition | Both use `Microsoft.IdentityModel.Tokens` primitives and the same JWS algorithm set; tests in `Kun.Identity.Tests` + `Kun.Security.Tests` cover round-trips on both sides. |

---

## Follow-ups

1. **Migrate `neuroagent-csharp` services** off their hand-rolled
   `AddAuthentication().AddJwtBearer` helpers onto
   `AddKunIdentity(id => id.AddJwtBearer(...))`.
2. **Retire Edge's hand-rolled OIDC code exchange** in favour of
   `IOidcClient.ExchangeCodeAsync`.
3. **Add refresh-token + client-credentials grants** to
   `KunOidcClient` once a consumer requires them.
4. **Land the `Kun.Security` → `Kun.Identity` bridge**: have
   `Kun.Security.S2SJwtIssuer` implement `Kun.Identity.IJwtIssuer`
   (or the reverse) so services can pick one canonical abstraction.
5. **Consider `IPermissionChecker`** — ADR-001 flagged this as the
   specific trigger for a revisit. Still out of scope here; capture
   in a future ADR when a second consumer needs it.

---

## References

- Handoff: task brief P1-KN-Identity
- [ADR-001](./ADR-001-Kun.Identity-module-decision.md) — the decision this ADR partially supersedes
- `Kun.Security/S2SJwt/*` — existing S2S issuer / verifier that stays in place
- `Kun.Gateway/S2sTokenIssuer.cs` — existing gateway-internal issuer, future follow-up to unify under `Kun.Identity.IJwtIssuer`
- `Microsoft.IdentityModel.JsonWebTokens.JsonWebTokenHandler` — the AOT-clean 8.x primitive this module is built on

## 主体类型字段补充（S1-KUN-013）

ICurrentUser.PrincipalKind 属于当前身份的最小字段，来源是已经认证的 principal_kind claim，不是浏览器专有字段，也不是权限集合。依据当前身份扩展指南，字段放在已有 ICurrentUser 上，IAuthenticatedUser 继承，不增加第三个身份接口。

旧实现通过默认接口值 user 保持兼容；框架 JWT / Cookie 适配器保留真实类型，无类型声明的旧浏览器身份按 user。权限执行点接受 user/service/agent 的显式类型，歧义/未知值拒绝，不能把机器服务悄悄当作用户。Agent 仅保留其主体维度，不因此开放 S2S 签发。主身份的 sub 与 NameIdentifier 若同时存在须一致，关键 claim 不能从次身份补齐。
