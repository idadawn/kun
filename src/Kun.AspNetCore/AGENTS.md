<!-- Parent: ../AGENTS.md -->
<!-- Generated: 2026-04-18 | Updated: 2026-04-18 -->

# Kun.AspNetCore

## Purpose

Host-layer integration with ASP.NET Core Minimal APIs. Provides DI registration extensions (`AddKunFramework`, `AddKunMediator`, `UseKunExceptionHandling`), Minimal API endpoint helper extensions that translate `Result<T>` into the correct HTTP status code, pipeline behaviors applicable to HTTP hosts (logging/correlation, request validation integration), and the global exception handler.

## Key Files

| File | Description |
|------|-------------|
| `Kun.AspNetCore.csproj` | `IsAotCandidate=true` |

## For AI Agents

### Working In This Directory

- **Minimal API only** in this module's extensions — no MVC controllers. AOT + Native AOT support for MVC is inferior; Minimal APIs are first-class.
- **`Result<T>` → HTTP status mapping** is centralized here (shared table with the gRPC interceptor in `Kun.Grpc`):
  - `Success` → 200/201/204 as appropriate
  - `Validation` → 400
  - `NotFound` → 404
  - `Conflict` → 409
  - `Unauthorized` → 401
  - `Forbidden` → 403
  - `Unavailable` → 503
  - `Failure` → 500
- **Do not use `app.MapControllers()` or `[ApiController]` attributes** — stick with `app.MapGet/Post/...`.
- OpenAPI generation uses `Microsoft.AspNetCore.OpenApi` (source-generated), not Swashbuckle.

### Testing Requirements

End-to-end tests via `Microsoft.AspNetCore.Mvc.Testing`'s `WebApplicationFactory` belong in `tests/Kun.IntegrationTests`. Assert HTTP status codes and JSON shapes.

### Common Patterns

- Endpoints receive `ISender` via DI and forward to `Send(command, ct)` / `Send(query, ct)`.
- `TypedResults` preferred over `Results` for AOT-friendly return typing.

## Dependencies

### Internal

- `Kun.Mediator` — `ISender`, `IPublisher`.
- `Kun.Core` — `Result`.

### External

- `Microsoft.AspNetCore.OpenApi`
- `Microsoft.AspNetCore.Authentication.JwtBearer`

<!-- MANUAL: -->
