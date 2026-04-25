<!-- Parent: ../AGENTS.md -->
<!-- Generated: 2026-04-18 | Updated: 2026-04-18 -->

# Results

## Purpose

The `Result` pattern: `Result`, `Result<T>`, `Error` (code + message + type), `ErrorType` (maps to HTTP/gRPC status codes), and `ResultFailureException` (a bridge type that gRPC and exception-handler middleware translate back into status codes).

## Key Files

| File | Description |
|------|-------------|
| `Result.cs` | `Result` (no value) and `Result<T>` (with `Value`, `EnsureSuccess()`); both readonly record structs |
| `Error.cs` | `Error(Code, Message, Type)` record + static factories per `ErrorType` |

## For AI Agents

### Working In This Directory

- **`Result<T>` is a value type (`readonly record struct`)** — no heap allocation. Keep it that way.
- **`Value` throws on failure access**; handlers must branch on `IsSuccess` before touching `Value`. The only shortcut is `EnsureSuccess()`, reserved for boundaries (gRPC endpoints) where the interceptor translates `ResultFailureException` into `RpcException`.
- **Implicit conversions** (`T → Result<T>`, `Error → Result<T>` / `Result`) keep handler code short. Don't remove them without auditing handler sites.
- **`Error.Code` is a stable, machine-readable string** (e.g. `order.notFound`). Use dotted namespaces so client code can match on a prefix.
- **`ErrorType` values** must align 1-to-1 with the mapping tables in `Kun.AspNetCore` and `Kun.Grpc`. Adding a new `ErrorType` requires updating both mapping tables in the same PR.

### Testing Requirements

Unit tests live in `tests/Kun.Core.Tests`. Cover: construction invariants (success cannot carry a non-`None` error, failure must carry an error), `Value`-throws-on-failure, implicit conversions, `EnsureSuccess` throwing `ResultFailureException`.

### Common Patterns

- Handler style (without extensions — still valid for simple cases):
  ```csharp
  var order = await _repo.GetByIdAsync(cmd.OrderId);
  if (order is null) return Error.NotFound("order.notFound", "Order not found");
  order.Cancel();
  await _uow.SaveChangesAsync(ct);
  return Result.Ok();
  ```

- Railway composition via the ADR-011 extensions. Use `Map` / `Bind` /
  `Tap` / `Ensure` / `Match` (and their `*Async` counterparts on
  `Task<Result<T>>` / `ValueTask<Result<T>>` sources) when a handler
  has 2+ sequential steps with short-circuit semantics. Failure
  inputs bypass the delegate; the carried `Error` is preserved.
  ```csharp
  // Multi-step async handler using the Task<Result<T>> extensions.
  return await _users.GetAsync(cmd.UserId, ct)              // Task<Result<User>>
      .EnsureAsync(u => u.IsActive, Error.Forbidden("user.inactive", "…"))
      .BindAsync((u, c) => _policy.EvaluateAsync(u, c), ct) // Task<Result<Policy>>
      .MapAsync(p => new SessionDto(p.Scope));              // Task<Result<SessionDto>>
  ```

- Side-effects (logging, metrics) go through `Tap` / `TapAsync` —
  they return the input unchanged so the chain continues:
  ```csharp
  return Result<int>.Ok(42)
      .Tap(v => _logger.LogInformation("produced {Value}", v))
      .Map(v => v * 2);
  ```

- `Match` / `MatchAsync` are terminal folds — return a single value
  for both branches (e.g. mapping to `IResult` at a Minimal API
  boundary):
  ```csharp
  return await command.HandleAsync(ct).MatchAsync(
      onSuccess: () => Results.Ok(),
      onFailure: err => Results.BadRequest(err.Code));
  ```

- **`CancellationToken` is required on async-delegate variants.** Pass
  `default` or `CancellationToken.None` if you truly have no token to
  forward. See ADR-011 §CancellationToken semantics for the RS0026
  rationale.

- **Pick the right source type**: `Task<Result<T>>` is the standard;
  `ValueTask<Result<T>>` is for hot-path Mediator handlers / Saga
  step executors that want to avoid `Task` heap allocation. The two
  extension families are **not interchangeable** — you cannot mix
  `.TaskResultExtensions.MapAsync` against a `ValueTask<…>` source.

## Dependencies

### Internal

None.

<!-- MANUAL: -->
