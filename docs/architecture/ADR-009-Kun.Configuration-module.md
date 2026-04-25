# ADR-009: Introduce `Kun.Configuration` Module

**Status**: Accepted
**Date**: 2026-04-23
**Author**: claude-kun (Kun framework specialist)
**Context**: Every consumer service in `neuroagent-csharp` (Edge, Platform, Notification, Storage, Gateway) reads strongly-typed options from configuration, but the wiring has drifted across three patterns: `Configure<T>(configuration.GetSection(...))` without any validation, hand-rolled `IValidateOptions<T>` implementations that miss cross-cutting attributes, and an inconsistent mix of `[Required]` from `System.ComponentModel.DataAnnotations` with `.ValidateDataAnnotations()` sometimes attached and sometimes not. The resulting failure modes at startup — silent defaults for missing secrets, runtime `NullReferenceException` on first use, or obscure "The field X is required" errors that don't say *where* X lives in `appsettings.json` — have cost operators real time.

---

## Decision

Introduce `src/Kun.Configuration/` (`IsAotCandidate=true`) with a
single DI entry point:

```csharp
services.AddKunOptions<AuthentikOptions>("Authentik");
```

This wires in one call:

1. `services.AddOptions<T>()` — the Options builder.
2. `.BindConfiguration("Authentik")` — bind `Authentik:*` keys to `T`.
3. A custom `IValidateOptions<T>` (`KunOptionsValidator<T>`) that
   runs DataAnnotations attributes plus any registered
   `IKunConfigurationValidator<T>`, and prefixes every error with
   the configured section path.
4. `.ValidateOnStart()` — `IHostedService`-driven fail-fast at host
   start.

Plus three attributes tuned for configuration:

| Attribute | Behaviour | Error shape |
|-----------|-----------|-------------|
| `[RequiredConfig]` | `[Required]` with `AllowEmptyStrings = false`. | `'ClientId' is required but missing in configuration.` |
| `[UrlConfig]` | `Uri.TryCreate(value, UriKind.Absolute, ...)`; null accepted so callers can pair with `[RequiredConfig]`. | `'Authority' must be a valid absolute URL.` |
| `[NonEmptyStringConfig]` | Rejects whitespace-only strings; null accepted so callers can pair with `[RequiredConfig]`. | `'ClientId' must be a non-empty string.` |

The failure messages surfaced by `KunOptionsValidator<T>` are
prefixed with the section path:

```
Authentik:ClientId — 'ClientId' is required but missing in configuration.
Authentik:Authority — 'Authority' must be a valid absolute URL.
Database:Primary:ConnectionString — 'ConnectionString' must be a non-empty string.
```

The operator now has the exact key to fix in `appsettings.json`.

---

## Why DataAnnotations over FluentValidation

| Dimension | DataAnnotations | FluentValidation |
|-----------|----------------|-------------------|
| AOT posture | Reflection-based but quarantinable behind `[RequiresUnreferencedCode]` + `[DynamicallyAccessedMembers(All)]` | Entirely reflection-based; not AOT-clean without significant source-gen investment |
| API familiarity | Already used across `neuroagent-csharp` | Would add a new validator DSL for consumers to learn |
| Startup cost | Built into `Microsoft.Extensions.Options.DataAnnotations` | Additional package + scan |
| Cross-field rules | Custom `ValidationAttribute` works but is clunky | First-class |

Primary path is DataAnnotations. The `IKunConfigurationValidator<T>`
interface is the escape hatch — consumers who need cross-field or
async validation implement it and register; `KunOptionsValidator<T>`
merges the results with DataAnnotations failures. If FluentValidation
demand materialises later, a `Kun.Configuration.FluentValidation`
adapter (sibling module) can register a `FluentValidationConfigurator<T>`
that implements the same interface — no change to the core.

---

## Module Shape and AOT

- Layer: **Abstraction** — depends only on `Microsoft.Extensions.*`
  Options / Configuration / Hosting abstractions.
- `<IsAotCandidate>true</IsAotCandidate>`. The `AddKunOptions<T>`
  entry point is annotated with `[RequiresUnreferencedCode]` +
  `[RequiresDynamicCode]` and the type parameter carries
  `[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]`
  so the trimmer preserves the properties DataAnnotations walks.
  The Configuration-Binder source-generator diagnostics
  `SYSLIB1100/1101/1104` are suppressed in the csproj with a
  targeted `NoWarn` — they fire on the unbounded-generic
  `BindConfiguration<TOptions>` call by design, and the runtime
  reflection fallback is the contract callers have already opted
  into via the `RequiresDynamicCode` attribute.
- If a future configuration-binder upgrade regresses the AOT
  posture, **stop-gate the upgrade**; do not silently flip the
  module to `IsAotCandidate=false`.

---

## Alternatives Considered

| Alternative | Why Rejected |
|-------------|--------------|
| A. Let every consumer call `AddOptions<T>().BindConfiguration().ValidateDataAnnotations().ValidateOnStart()` manually | That is the status quo being replaced — three drift paths have already been observed. |
| B. Skip DataAnnotations and build a bespoke validator interface | Would force consumers to learn a new validation idiom; loses the familiar `[Required]`/`[Url]` shape. |
| C. Adopt FluentValidation as the primary path | Not AOT-clean; extra dependency; new DSL to teach. Kept as a potential sibling module for the escape hatch. |
| D. **DataAnnotations + IKunConfigurationValidator<T> escape hatch (selected)** | Primary path covers 90% of cases in a familiar idiom; custom validator lets consumers add cross-field / async rules without fighting the framework. |

---

## Consequences

### Positive

- One-line fail-fast startup for typed options everywhere.
- Error messages name the config path; operators stop triaging
  "which `ClientId`?".
- Framework-provided attributes (`RequiredConfig`, `UrlConfig`,
  `NonEmptyStringConfig`) replace per-service helpers.
- Custom cross-field validation remains possible via the escape
  hatch.

### Negative / Trade-offs

- `AddKunOptions<T>` carries `[RequiresUnreferencedCode]` — AOT-strict
  consumers propagate the attribute up the call chain or accept the
  trim warning. Documented in AGENTS.md.
- The `NoWarn` list suppresses real source-generator output that a
  hypothetical future concrete-type API could avoid; acceptable
  because `AddKunOptions` is the only public surface and its
  reflection contract is explicit.

### Risks

| Risk | Mitigation |
|------|------------|
| ConfigurationBinder / Options upgrade breaks the suppression shape | CI AOT matrix includes `Kun.Configuration`; stop-gate rule applies. |
| Consumer forgets to apply `[DynamicallyAccessedMembers(All)]` on a custom options type under AOT | The type-parameter annotation on `AddKunOptions<T>` propagates the constraint; callers get an analyzer warning at the call site. |

---

## Follow-ups

1. **Migrate `neuroagent-csharp/ServiceDefaults`** off hand-rolled
   `Configure<T>(GetSection(...))` onto `AddKunOptions<T>`.
2. **FluentValidation adapter module** (`Kun.Configuration.FluentValidation`)
   if a second consumer needs cross-field / async rules that are
   painful to express as attributes.
3. **`IOptionsMonitor<T>` hot-reload validation** — currently
   `ValidateOnStart` runs once; hot-reload failures do not
   re-trigger validation. If a consumer requires it, wire a
   `IOptionsChangeTokenSource<T>` + re-run `KunOptionsValidator<T>`
   on change.

---

## References

- Handoff: task brief P1-KN-Configuration
- `Microsoft.Extensions.Options.DataAnnotations` — the underlying
  library this module builds on
- `System.ComponentModel.DataAnnotations.ValidationAttribute` — the
  custom-attribute base
- ADR-005 (Kun.Caching) — sibling example of an AOT-candidate
  module with opt-in reflection via `[RequiresUnreferencedCode]`
