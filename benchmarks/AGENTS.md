<!-- Parent: ../AGENTS.md -->
<!-- Generated: 2026-04-23 | Updated: 2026-04-23 -->

# benchmarks

## Purpose

BenchmarkDotNet performance baselines for Kun Framework critical paths.
These benchmarks are the regression guardrail for Phase 2+ refactors —
when a framework change lands that touches `Kun.Caching`, `Kun.Storage`,
`Kun.Audit.EntityFrameworkCore`, or `Kun.MultiTenancy.EntityFrameworkCore`,
the submitter is expected to attach the before/after benchmark delta
against `docs/benchmarks/baseline-*.md`.

## Subdirectories

| Directory | Purpose |
|-----------|---------|
| `Kun.Benchmarks/` | Single benchmark assembly. `Program.cs` is the BenchmarkDotNet entry point; benchmarks live in `*Benchmarks.cs` files. |
| `Kun.Benchmarks.Grpc/` | Microbenchmarks for `Kun.Grpc` interceptor hot paths. Run weekly (plus on `benchmarks`-labeled PRs) by `.github/workflows/benchmarks.yml`. |

## For AI Agents

### Working In This Directory

- **Never run benchmarks on the same machine as `dotnet test` in parallel** — BenchmarkDotNet spawns per-benchmark child processes and competes for CPU. If you need automation, either run in a dedicated CI job (see `.github/workflows/benchmarks.yml`) or gate the run behind a `[Release]` config.
- BenchmarkDotNet requires `-c Release`. Running under `Debug` will refuse to execute and print a validation error.
- Every new benchmark class must keep the ShortRunJob budget: < 3 minutes wall-clock for the full suite on a developer laptop. Use `[SimpleJob(RunStrategy.Throughput)]` only for microbenchmarks; mark anything needing a real broker (Redis / S3) with a skip guard, not a hard dependency.
- Fakes > containers: for Redis / S3 comparisons, prefer in-process fakes that model the network-free code path. Add a `// FAKE:` comment on the method explaining what the fake *does not* simulate (wire latency, server-side cost) so readers don't over-interpret numbers.
- When baselines shift by more than 10 % on the CI baseline workflow, update `docs/benchmarks/baseline-<YYYY-MM-DD>.md` and include a paragraph explaining the root cause.

### Running Locally

```bash
# Full suite (ShortRunJob, ~1–3 min wall-clock)
dotnet run -c Release --project benchmarks/Kun.Benchmarks -- --filter '*'

# A single benchmark class
dotnet run -c Release --project benchmarks/Kun.Benchmarks -- --filter '*CachingBenchmarks*'

# List without running
dotnet run -c Release --project benchmarks/Kun.Benchmarks -- --list flat
```

Artifacts land under `BenchmarkDotNet.Artifacts/results/` (both `.json` and `.md`).

### Testing Requirements

Benchmarks are not unit tests — they are not executed by `dotnet test`. The
CI `build` job still compiles `Kun.Benchmarks` via the solution-level
`dotnet build`, so a syntax regression here will fail CI the same way a
test project would.

## Dependencies

### Internal

- `Kun.Caching`, `Kun.Storage`, `Kun.Audit`, `Kun.Audit.EntityFrameworkCore`, `Kun.MultiTenancy`, `Kun.MultiTenancy.EntityFrameworkCore`, `Kun.EntityFrameworkCore` — the benchmark targets.

### External

- `BenchmarkDotNet` 0.14.x
- `Microsoft.EntityFrameworkCore.InMemory` (for audit / tenant filter benchmarks)
- `Microsoft.Extensions.Caching.Memory` (for the in-process cache baseline)

<!-- MANUAL: -->
