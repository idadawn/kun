# ADR-014: Result-returning validation pipeline behavior

**Status**: Accepted
**Date**: 2026-04-24
**Author**: claude-kun (Kun framework specialist)
**Extends**: [ADR-011](./ADR-011-Result-async-extensions.md) — consumer
of the `Result<T>` composition surface.
**Supersedes**: none

**Context**: `src/Kun.Validation/Behaviors/ValidationPipelineBehavior.cs`
is the **exception-throwing** validator pipeline behavior: any
validator error turns into `ValidationException`. That matches how
validators in most .NET codebases have historically worked, but it
conflicts with the Kun-wide rule in `kun/CLAUDE.md`
§Architectural constraints:

> Business failures use `Result` / `Result<T>`, not exceptions.

Handlers that return `Result<T>` still have to sit behind a
try/catch for `ValidationException` at every call site, or trust
ASP.NET / gRPC middleware to translate the exception — both of
which mask the failure at the handler-composition layer ADR-011
tried to make ergonomic.

The throwing behavior's own XML doc already called this out:

> For Result-based semantics (returning
> `Result.Fail(Error.Validation(...))` instead of throwing), compose
> this behavior inside a user-written "ValidationResultAdapter"
> behavior — shipped as a follow-up.

KN-SELF-007 is that follow-up.

---

## Decision

Ship a second pipeline behavior,
`ValidationResultPipelineBehavior<TRequest, TValue>`, that runs the
validators in the same loop the throwing variant uses but on
aggregated errors **returns** `Result<TValue>.Fail(Error.Validation(
"request.invalid", …))` instead of throwing. Handlers downstream
never see an exception for business-validation failures.

### Scope — `Result<TValue>` only

Only the generic `Result<TValue>` variant ships in this task. The
non-generic `Result` variant (for commands with no return value) is
deliberately out of scope:

- Most Kun handlers return a value. The dominant pattern is
  `IRequest<Result<OrderDto>>`, not `IRequest<Result>`.
- Adding the non-generic variant doubles the DI-registration
  surface (one helper per constrained TResponse).
- Void-returning commands keep working with the throwing
  `ValidationPipelineBehavior`; the exception-path cost is
  acceptable for a small minority.

Tracked as **F1** below.

### DI registration — explicit, per request type

A behavior constrained as
`IPipelineBehavior<TRequest, Result<TValue>> where TRequest :
IRequest<Result<TValue>>` cannot be registered as an open-generic
against the full `IPipelineBehavior<,>` surface — the DI container
would try to close it for every `(TRequest, TResponse)` pair and
hit a runtime type-system failure for non-Result responses.

Instead, consumers register per request type:

```csharp
services.AddKunMediator(mediator =>
{
    mediator
        .AddValidator<CreateOrderCommand, CreateOrderValidator>()
        .AddValidationResultBehavior<CreateOrderCommand, OrderDto>();
});
```

This mirrors the existing
`AddValidator<TRequest, TValidator>()` pattern — explicit,
greppable, AOT-safe.

### Coexistence with the throwing variant

Consumers pick one or the other **per request type**. Registering
both behaviors for the same `TRequest` would result in
non-deterministic order-dependent behavior (first one in the pipeline
wins; the second runs on the already-converted result). We do not
ship a runtime diagnostic for this misconfiguration because:

- Detecting it cleanly would require enumerating all registered
  pipeline behaviors at Mediator build time — not currently a public
  hook.
- The misconfiguration is rare and loud once it occurs (double-wrapped
  validation messages stand out).

Doc guardrail only — `src/Kun.Validation/AGENTS.md` gets a "Result vs
exception" subsection pointing at this ADR.

### Error shape

Aggregated `ValidationError.Message` strings are joined with `"; "`
into the `Error.Message`. `Error.Code` is the constant
`"request.invalid"`. Consumers who need structured error payloads
(per-field error dicts) can post-process the returned `Result` via
ADR-011 `.Match` / `.Bind` — the framework does not ship a second
`Error` shape per validator, because the dominant consumer behavior
(gRPC 400, HTTP 400 JSON body) is already served.

### AOT safety

- No reflection. `typeof(Result<TValue>)` is compile-time-known per
  closed generic.
- `Error.Validation` and `Result<TValue>.Fail` are pure BCL — no
  source-generator dependency.
- The validator loop is the same shape the throwing variant uses
  and has been AOT-green since Kun.Validation shipped as
  `IsAotCandidate=true`.

---

## Alternatives considered

### One behavior that catches `ValidationException` and converts

Catch the existing exception and translate to `Result.Fail`. Works
without requiring a separate behavior but keeps the
throw-and-catch overhead (allocating the exception, building its
stack trace). Rejected — defeats the performance argument for using
`Result` and hides the conversion from code review.

### Open-generic DI registration with a runtime type check

Register `IPipelineBehavior<,>` → `ValidationResultPipelineBehavior<,>`
and have the behavior short-circuit when `TResponse` isn't
`Result<T>`. Rejected — type-system constraints at the registration
level are cleaner than behavior-body runtime checks, and consumer
code composing the pipeline would silently skip validation for any
misregistered closure.

### Modify the existing throwing behavior in place

Change the current `ValidationPipelineBehavior` to return `Result`
when the response type matches, throw otherwise. Rejected — silent
behavioural change to an already-shipped extension surface; would
require a CHANGELOG breaking note and a migration guide. Much
safer to ship a sibling behavior.

### Provide a source generator that emits a registration per handler

Scan the assembly for `IRequestHandler<*, Result<*>>` and register
the right behavior closure. Rejected for this ADR — source
generators are deferred Kun-wide per `docs/roadmap.md` §环境与流程类
until M5 post-review. The explicit `AddValidationResultBehavior`
extension is the short-term surface; an SG can wrap it later.

---

## Follow-ups

- **F1**: Non-generic `Result` variant —
  `ValidationResultPipelineBehavior<TRequest>` for
  `IRequest<Result>`. Ships when a consumer returns `Result` from
  a command often enough to be worth the doubled registration
  surface.
- **F2**: Structured error payload (`Error.Validation` subtype
  carrying a per-field error dict). Defer until consumers have a
  concrete need past the joined-message default.
- **F3**: Convenience extension that registers the behavior for
  every `IRequestHandler<*, Result<*>>` discovered in an assembly.
  Requires source generator — see Alternatives §Modify-existing.

---

## Impact on existing code

Purely additive. The existing throwing
`ValidationPipelineBehavior` is untouched. Consumers not opting in
see no change. `Kun.Validation.csproj` gains no new
`PackageReference`.
