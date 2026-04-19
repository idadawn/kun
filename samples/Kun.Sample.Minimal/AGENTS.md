<!-- Parent: ../AGENTS.md -->
<!-- Generated: 2026-04-18 | Updated: 2026-04-18 -->

# Kun.Sample.Minimal

## Purpose

Minimal AOT-publishable console program wiring `KunMediator` with the three built-in pipeline behaviors (`LoggingBehavior`, `PerformanceBehavior`, `TracingBehavior`). It doubles as:

1. An on-ramp for users learning the CQRS dispatch API (one file, no ceremony)
2. The M1 AOT canary — every PR touching `Kun.Core` or `Kun.Mediator` should confirm this sample still publishes AOT with zero trim warnings

## Key Files

| File | Description |
|------|-------------|
| `Kun.Sample.Minimal.csproj` | `OutputType=Exe`, `PublishAot=true`, `IsAotCandidate=true`, `InvariantGlobalization=true`, references Kun.Core + Kun.Mediator |
| `Program.cs` | Top-level-statement composition root: defines `GreetCommand` / `NowQuery` / `UserJoined`, wires handlers + three behaviors, exercises `ISender` + `IPublisher`, listens on `ActivitySource("Kun.Mediator")` |

## For AI Agents

### Working In This Directory

- **Keep the sample under ~150 lines.** Its value is clarity — users read it end-to-end to understand how `KunMediator` is wired. Don't add features unless they're framework-level concepts not yet demonstrated.
- Everything runs via `NullLogger<T>` (from `Microsoft.Extensions.Logging.Abstractions`) to avoid pulling in the concrete `Microsoft.Extensions.Logging` package. Keep it that way.
- File-local types (`file sealed record` / `file sealed class`) keep the sample single-file. Don't extract them into separate files without a reason.
- When M1's SourceGenerator eventually lands, this sample should migrate to it (delete the manual `AddRequestHandler<,,>()` calls, add a single `AddKunMediator(typeof(GreetCommand).Assembly)` equivalent).

### Prerequisites for `dotnet publish -p:PublishAot=true`

Native AOT publish requires a working platform linker:

- **Windows**: install Visual Studio Build Tools with the "Desktop Development for C++" workload (provides `link.exe`). For ARM64, also install the C++ ARM64 build tools. See <https://aka.ms/nativeaot-prerequisites>.
- **Linux (ubuntu-latest in CI)**: the required toolchain is in the default image; no extra setup.
- **macOS**: requires Xcode command-line tools.

The managed-IL trim analysis phase of the ILC compiler runs *before* the native linker, so `dotnet build` and the standard `TreatWarningsAsErrors=true` are enough to catch every reflection / trimming issue. The native link step is a packaging concern, not a correctness concern.

### Testing Requirements

```bash
dotnet run --project samples/Kun.Sample.Minimal -c Release
```

Expected output:
- Greeting line from `GreetCommand` handler
- ISO-8601 timestamp from `NowQuery` handler
- Two notification handlers ("EmailSender", "AnalyticsTracker") firing in registration order
- `[Activity] Mediator.Send ... — <ms>` line from the tracing listener

AOT publish (on a host with a platform linker):

```bash
dotnet publish samples/Kun.Sample.Minimal -c Release -p:PublishAot=true -p:TreatWarningsAsErrors=true
```

Expected: zero warnings, single native executable under `bin/Release/net10.0/<rid>/publish/`.

## Dependencies

### Internal

- `Kun.Core` — CQRS contracts, `ISystemClock`
- `Kun.Mediator` — dispatcher + built-in behaviors

### External

- `Microsoft.Extensions.DependencyInjection` (concrete container for `ServiceCollection`)
- `Microsoft.Extensions.Logging.Abstractions` (for `NullLogger<T>`)

<!-- MANUAL: -->
