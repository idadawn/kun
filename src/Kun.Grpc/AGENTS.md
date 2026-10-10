<!-- Parent: ../AGENTS.md -->
<!-- Generated: 2026-04-18 | Updated: 2026-04-18 -->

# Kun.Grpc

## Purpose

Host-layer integration with gRPC (Grpc.AspNetCore). Provides three server interceptors and a DI helper for wiring them into a gRPC host:

- **`KunResultToRpcInterceptor`** — catches `ResultFailureException` (thrown by `Result<T>.EnsureSuccess()`) and translates it into the corresponding `RpcException` status code.
- **`KunServerAuthInterceptor`** — enforces that every request has an authenticated `ClaimsPrincipal` with a nonblank `tenant_id` claim before entering a handler or reading a stream frame. Unary, client streaming, server streaming and duplex all use this boundary.
- **`KunTracingInterceptor`** — starts a diagnostic `Activity("grpc.server")` per RPC call, covering the full awaited lifetime of all four RPC shapes, tagged with `rpc.system=grpc`, `rpc.method`, `rpc.type` and `rpc.grpc.status_code`.

## Key Files

| File | Description |
|------|-------------|
| `Kun.Grpc.csproj` | `IsAotCandidate=true`, `HasPublicApi=true`. References `Kun.Core`, `Kun.Security`, `Grpc.AspNetCore`, `Microsoft.AspNetCore.App` framework ref. |
| `Interceptors/KunResultToRpcInterceptor.cs` | `internal sealed class`. Static helpers `MapErrorType(ErrorType)` and `ToRpcException(Error)` are testable via `InternalsVisibleTo`. |
| `Interceptors/KunServerAuthInterceptor.cs` | `internal sealed class`. Checks `context.GetHttpContext().User` for `IsAuthenticated` and `tenant_id` claim. |
| `Interceptors/KunTracingInterceptor.cs` | `internal sealed class`. Uses `ActivitySource("Kun.Grpc")`. |
| `DependencyInjection/KunGrpcServiceCollectionExtensions.cs` | Public API: `AddKunGrpc(IServiceCollection, Action<GrpcServiceOptions>?)`. Registers all three interceptors in order: Tracing → Auth → ResultMapping. |
| `PublicAPI.Unshipped.txt` | Tracks public surface: `KunGrpcServiceCollectionExtensions` + `AddKunGrpc`. |

## For AI Agents

### Working In This Directory

- **Result → gRPC status mapping**:
  - `Validation`   → `StatusCode.InvalidArgument`
  - `NotFound`     → `StatusCode.NotFound`
  - `Conflict`     → `StatusCode.FailedPrecondition`
  - `Unauthorized` → `StatusCode.Unauthenticated`
  - `Forbidden`    → `StatusCode.PermissionDenied`
  - `Unavailable`  → `StatusCode.Unavailable`
  - `Failure` / unknown → `StatusCode.Internal`
- The three interceptors are **`internal`** (not `public`) — the public API analyzer (RS0016/RS0017) cannot track overrides of generic virtual methods from external base classes. They are exposed to `Kun.Grpc.Tests` via `InternalsVisibleTo`.
- Handler code returns `Result<T>`; gRPC endpoints call `.EnsureSuccess()` and let the interceptor do the translation. Don't `try/catch (ResultFailureException)` inside the endpoint.
- `Kun.Security.DependencyInjection.KunSecurityServiceCollectionExtensions.AddKunSecurity()` must be called separately to wire up `JwtTenantContext`/`JwtCurrentUser`.
- gRPC 2.67.x in this repo — use `Grpc.AspNetCore` server side, `Grpc.Net.Client` client side.

### Testing Requirements

- Unit tests live in `tests/Kun.Grpc.Tests/ResultMappingTests.cs` — cover all `ErrorType` → `StatusCode` mappings.
- Integration tests with real gRPC channels live in `tests/Kun.IntegrationTests`.

### Common Patterns

```csharp
// In host startup:
builder.Services.AddKunSecurity();
builder.Services.AddKunGrpc();
```

The interceptors execute in registration order: Tracing → Auth → ResultMapping (innermost).

## Dependencies

### Internal

- `Kun.Core` — `Result`, `Error`, `ErrorType`, `ResultFailureException`.
- `Kun.Security` — `JwtTenantContext`, `JwtCurrentUser` (registered separately via `AddKunSecurity()`).

### External

- `Grpc.AspNetCore` (server-side gRPC, `Grpc.Core.Interceptors.Interceptor`)
- `Microsoft.AspNetCore.App` (framework reference — `HttpContext`, `ServerCallContext.GetHttpContext()`)

<!-- MANUAL: -->
