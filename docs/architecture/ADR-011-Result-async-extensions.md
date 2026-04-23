# ADR-011: `Result<T>` composition extensions (sync + async)

**Status**: Accepted
**Date**: 2026-04-24
**Author**: claude-kun (Kun framework specialist)
**Extends**: none (first ADR dedicated to `Kun.Core.Results`)
**Supersedes**: none

**Context**: `Kun.Core.Results.Result<T>` ships today with only `Ok` / `Fail`
/ `EnsureSuccess`. Handlers that compose more than one step have to
hand-roll the short-circuit plumbing every time:

```csharp
var user = await _userRepo.GetAsync(userId, ct);
if (user is null) return Error.NotFound("user.notFound", "User not found");

var policy = await _policy.EvaluateAsync(user, ct);
if (policy.IsFailure) return policy.Error;

var session = await _sessions.CreateAsync(user, ct);
if (session.IsFailure) return session.Error;

return Result<SessionDto>.Ok(MapToDto(session.Value));
```

`neuroagent-csharp` Edge, Platform, and the forthcoming gateway
handlers all repeat variants of this. The drift is exactly what
ADR-010 warned about for cookie sessions — *the framework is
consumed as a dependency; every re-invented abstraction downstream
becomes a version skew in six months.* Land the canonical
composition shape at the core.

---

## Decision

Add extension methods on `Kun.Core.Results.Result` and
`Kun.Core.Results.Result<T>` covering the five canonical railway-
oriented operations — **Map, Bind, Tap, Ensure, Match** — in three
flavours:

1. **Synchronous**: extensions on `Result` / `Result<T>` receivers.
2. **Asynchronous — `Task<…>` sources**: extensions on
   `Task<Result>` / `Task<Result<T>>` receivers so callers can
   fluent-chain after `await`-producing calls.
3. **Asynchronous — `ValueTask<…>` sources**: extensions on
   `ValueTask<Result>` / `ValueTask<Result<T>>` receivers for hot-
   path handlers (`Kun.Mediator` pipeline, Saga step executors)
   that return `ValueTask` to avoid `Task` allocation.

All extensions live in a new namespace-preserving file set under
`src/Kun.Core/Results/`. They are **pure BCL** — no new
`<PackageReference>` — per the `Kun.Core` zero-third-party-dep
rule.

### Naming

| Op | Signature sketch | Intent |
|----|------------------|--------|
| `Map<T, TOut>` | `Result<T> → Func<T, TOut> → Result<TOut>` | Transform the success value; never fails |
| `Bind<T, TOut>` | `Result<T> → Func<T, Result<TOut>> → Result<TOut>` | Chain an op that can itself fail |
| `Tap` | `Result<T> → Action<T> → Result<T>` (pass-through) | Side effect on success, unchanged payload |
| `Ensure` | `Result<T> → Func<T, bool> → Error → Result<T>` | Add a post-condition; success with false predicate becomes the supplied `Error` |
| `Match<T, TOut>` | `Result<T> → Func<T, TOut> → Func<Error, TOut> → TOut` | Terminal fold into a single value |

For the non-generic `Result`, only the pass-through ops apply
(`Tap(Action)`, `Ensure(Func<bool>, Error)`, `Match<TOut>`) plus a
`Bind(Func<Result>)` for sequencing side-effectful steps.

Async method names are the sync name + **`Async` suffix**:
`MapAsync`, `BindAsync`, `TapAsync`, `EnsureAsync`, `MatchAsync`.

### Async overload matrix

For every async method on every source type:

| Source | Delegate shape | CT? |
|--------|---------------|:---:|
| `Task<Result<T>>` | sync `Func<T, TOut>` | no |
| `Task<Result<T>>` | async `Func<T, CancellationToken, Task<TOut>>` | yes |
| `ValueTask<Result<T>>` | sync `Func<T, TOut>` | no |
| `ValueTask<Result<T>>` | async `Func<T, CancellationToken, ValueTask<TOut>>` | yes |

Rationale for **exactly two delegate variants** per (source × op)
cell:

- The sync-delegate overload is essential because the common
  downstream pattern is `await RepoAsync()` (returns
  `Task<Result<T>>`) **then** a cheap pure transform (`Map`,
  `Ensure`). Forcing callers into an async lambda for a sync
  transform would add needless `async`/`await` state-machine
  overhead.
- The async-delegate overload with `CancellationToken` covers the
  `await` → another `await` chain. The `CancellationToken` is
  always the **last** parameter with a `default` so chained calls
  that want to forward it can, and callers that don't need it
  don't have to care.
- `Func<T, Task<TOut>>` without CT is **not** added — if you have an
  async delegate you should be accepting a CT. The compiler will
  still bind to the CT-accepting overload with a throwaway
  `(_, _) =>` lambda, at the minor cost of explicit ceremony.

### Return types

- Sync ops on `Result<T>` return `Result<TOut>` or `Result<T>` per
  the shape above.
- Async ops on `Task<Result<T>>` sources return `Task<Result<TOut>>`
  or `Task<Result<T>>`.
- Async ops on `ValueTask<Result<T>>` sources return
  `ValueTask<Result<TOut>>` or `ValueTask<Result<T>>`.

Keeping the return-type family aligned with the source type
preserves fluent chaining without forcing `.AsTask()` /
`.AsValueTask()` boundary friction.

### Failure short-circuit semantics (normative)

- On every op, if the input `Result`/`Result<T>` is in a failure
  state, the delegate **must not** be invoked and the original
  `Error` is carried through unchanged.
- `Ensure` on a successful input invokes the predicate; if the
  predicate returns `false`, the output becomes a failure carrying
  the `Error` passed to `Ensure`. If the predicate returns `true`,
  the input is passed through unchanged.
- `Match` invokes exactly one of its two delegates — never both,
  never neither.

### `CancellationToken` semantics

- Async overloads accept `CancellationToken ct` and forward it
  unchanged to the supplied delegate.
- **Required, not optional.** The sketch above intended
  `CancellationToken ct = default` but the Roslyn PublicAPI analyzer
  rule `RS0026` ("Do not add multiple overloads with optional
  parameters") flags that combination. Rather than suppress the rule
  blanket-wide, the async-delegate overloads require an explicit CT;
  callers who want no token pass `default` or
  `CancellationToken.None`. The UX cost is small and the binary-
  compat safety is concrete — future ADR revisions may revisit if a
  pattern for safely suppressing RS0026 per-symbol emerges.
- A pre-cancelled token surfaces `OperationCanceledException` via
  the awaited delegate (or via `CancellationToken.ThrowIfCancellationRequested()`
  where no delegate is called). We deliberately do **not** wrap
  cancellation into a `Result` failure — cancellation is infra-
  signal not business failure, matching the project rule
  "exceptions are reserved for systemic failures".

### AOT safety

All extensions are pure generic instantiation — no reflection, no
`Expression` trees, no `MakeGenericMethod`. `Kun.Core` stays
AOT-candidate (`IsAotCandidate=true`). The `/warnaserror` publish
verification step in the Executor's build gate is non-negotiable.

### Public-API surface additions

Every new method is `public static`, XML-documented, and listed in
`src/Kun.Core/PublicAPI.Unshipped.txt`. Names above are normative
— any future rename requires an ADR that supersedes this one.

---

## Alternatives considered

### `Then` instead of `Bind`

F# / railway-oriented writeups often use `Then` (or `andThen`).
Rejected because .NET callers read `Then` as "do this next
unconditionally", which conflicts with `Bind`'s
failure-short-circuit semantics. `Bind` is explicit about "only
runs on success" to anyone who has met the pattern before.

### `Select` / `SelectMany` (LINQ syntax support)

Adding `Select` / `SelectMany` would unlock `from … in` syntax
for `Result<T>`. Rejected for this ADR: the LINQ sugar encourages
*query-style* composition that obscures the failure flow at the
call site. Handlers benefit from the explicit `.Bind(…).Map(…)`
chain — reviewers see where short-circuits happen. LINQ support
can be added later as a separate, opt-in namespace (`using
Kun.Core.Results.Linq;`) without breaking this ADR.

### Single generic async source type via `IResultSource<T>`

A wrapper abstraction that erases `Task` vs `ValueTask` was
considered. Rejected: it adds a heap allocation on every call,
defeats AOT-friendliness of `ValueTask`, and introduces a new
type for consumers to learn. The 2× duplicate surface (Task +
ValueTask) is mechanical and the test matrix covers both.

### `Flatten` (`Result<Result<T>>` → `Result<T>`)

Useful in combinator-heavy code; rejected as YAGNI for the
initial ADR. The common shape in handlers is `Bind`-and-done; no
observed case where a caller materialises `Result<Result<T>>`.
Revisit if a concrete consumer need appears.

### Implicit `Result`-of-`Result` unwrapping inside `Map`

Would let `Map` double-duty as `Bind` when the transform returns a
`Result<TOut>`. Rejected: conflates two behaviours, makes the
compiler's overload resolution pick the wrong one on innocent
refactors, and requires runtime type inspection (breaks AOT).

---

## Impact on existing code

- No existing code changes — this ADR is purely additive.
- `neuroagent-csharp` does **not** get migrated in this ADR's
  scope. A follow-up ralplan will sweep hand-rolled patterns
  after at least one `-beta` of Kun ships the extensions.
- `PublicAPI.Shipped.txt` stays untouched; new methods are all in
  `PublicAPI.Unshipped.txt` until the next release cut.
- `Kun.Mediator` pipeline is a likely early internal consumer
  (`TracingBehavior` / `ValidationBehavior` compose a chain of
  failures today); migrating those behaviours is **not** in scope
  for ADR-011 — also a follow-up.

## Security / observability considerations

- Extensions do not introduce logging, telemetry, or exception
  translation paths. `Kun.Observability` / `Kun.Grpc`
  interceptors continue to see `Result<T>` values and `Error`s
  unchanged.
- No claim data or tenant context flows through the extensions,
  so they are tenant-agnostic and safe in any caller context.

## Follow-ups

- **F1**: Migrate `neuroagent-csharp` Edge / Platform handlers off
  the hand-rolled pattern (separate P1 task, not self-managed).
- **F2**: Evaluate LINQ `Select` / `SelectMany` in a separate
  opt-in namespace once ADR-011 ships.
- **F3**: Consider analyzer to flag `if (result.IsFailure) return
  result.Error;` blocks where `Bind` / `Map` would be idiomatic —
  likely YAGNI until migration data shows real drift.
