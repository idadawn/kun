<!-- Parent: ../AGENTS.md -->
<!-- Generated: 2026-04-23 | Updated: 2026-04-23 -->

# Kun.Integration.AotTests

## Purpose

AOT publish validation — runs `dotnet publish -p:PublishAot=true` on
`samples/Kun.Sample.Minimal` and asserts the output is free of
trim / AOT / single-file analyzer diagnostics. When a platform
linker (`clang` / `gcc`) is available the native binary is
additionally executed and its canonical output is asserted.

## Key Files

| File | Description |
|------|-------------|
| `Kun.Integration.AotTests.csproj` | Test project (`IsAotCandidate=false`). Shells out to `dotnet publish`; does not directly reference the sample. |
| `SampleMinimalAotPublishTests.cs` | Two xUnit.v3 facts: `Publish_IsFree_OfTrimAndAotWarnings` and `PublishedBinary_RunsAndEmitsExpectedOutput`. |

## For AI Agents

### Working In This Directory

- **Do not add `ProjectReference` to `Kun.Sample.Minimal`.** We deliberately invoke the publish CLI so the test exercises exactly what a downstream consumer sees.
- `LocateRepoRoot()` walks up to find `Kun.sln`; if you rename the solution, update the check.
- Tests gracefully skip the native-run assertions when `clang` / `gcc` is missing (local dev). CI's `sample-aot-integration` job runs on `ubuntu-latest`, where the linker is available, and therefore fails hard on any publish regression.
- The regex `warning IL\d{4}` catches the full trim + AOT + single-file analyzer family. Do not narrow it.

### Testing Requirements

```bash
dotnet test tests/Kun.Integration.AotTests -c Release
```

### Common Patterns

- Process invocation uses `ArgumentList` (not a raw command string) to avoid shell-injection surprises in CI.
- Output directories go under `Path.GetTempPath()` with a `Guid.NewGuid()` suffix so parallel CI runs don't collide.

## Dependencies

### Internal

- None at compile time — Kun.Sample.Minimal is resolved at runtime via the filesystem.

### External

- `xunit.v3` + `xunit.runner.visualstudio`
- `FluentAssertions`
- `Microsoft.NET.Test.Sdk`

<!-- MANUAL: -->
