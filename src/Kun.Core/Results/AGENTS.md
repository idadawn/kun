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

- Handler style:
  ```csharp
  var order = await _repo.GetByIdAsync(cmd.OrderId);
  if (order is null) return Error.NotFound("order.notFound", "Order not found");
  order.Cancel();
  await _uow.SaveChangesAsync(ct);
  return Result.Ok();
  ```

## Dependencies

### Internal

None.

<!-- MANUAL: -->
