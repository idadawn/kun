<!-- Parent: ../AGENTS.md -->
<!-- Generated: 2026-04-18 | Updated: 2026-04-18 -->

# Kun.Grpc

## Purpose

Host-layer integration with gRPC (Grpc.AspNetCore). Provides `KunResultToRpcInterceptor` that catches `ResultFailureException` (thrown by `Result<T>.EnsureSuccess()`) and translates it into the corresponding `RpcException` status code, a logging interceptor that scopes `Activity` spans to the RPC method, and DI helpers for wiring both into a gRPC host.

## Key Files

| File | Description |
|------|-------------|
| `Kun.Grpc.csproj` | `IsAotCandidate=true` |

## For AI Agents

### Working In This Directory

- **Result → gRPC status mapping** is the counterpart to `Kun.AspNetCore`'s HTTP mapping:
  - `NotFound` → `StatusCode.NotFound`
  - `Validation` → `StatusCode.InvalidArgument`
  - `Conflict` → `StatusCode.FailedPrecondition`
  - `Unauthorized` → `StatusCode.Unauthenticated`
  - `Forbidden` → `StatusCode.PermissionDenied`
  - `Unavailable` → `StatusCode.Unavailable`
  - `Failure` / unknown → `StatusCode.Internal`
- **Handler code returns `Result<T>`; gRPC endpoints call `.EnsureSuccess()`** and let the interceptor do the translation. Don't `try/catch (ResultFailureException)` inside the endpoint.
- gRPC 2.67.x in this repo — use `Grpc.AspNetCore` server side, `Grpc.Net.Client` client side, and `Grpc.Tools` for `.proto` code generation.
- Protobuf-generated types must be registered in `KunJsonContext` if you serialize them to JSON for Outbox (rare — prefer sticking to framework integration event types for eventing).

### Testing Requirements

gRPC contract tests live in `tests/Kun.IntegrationTests`. Use `GrpcChannelForAddress(...)` against a `TestServer` to verify status-code mapping and happy-path RPC.

### Common Patterns

- `services.AddGrpc(o => o.Interceptors.Add<KunResultToRpcInterceptor>())` in host startup.
- Client side: `AddGrpcClient<T>(...)` + `AddKunServiceAuth()` extension from `Kun.Security`.

## Dependencies

### Internal

- `Kun.Mediator` — `ISender`.
- `Kun.Core` — `Result`, `Error`, `ResultFailureException`.

### External

- `Grpc.AspNetCore`, `Grpc.AspNetCore.Web`
- `Grpc.Net.Client`, `Grpc.Tools`
- `Google.Protobuf`

<!-- MANUAL: -->
