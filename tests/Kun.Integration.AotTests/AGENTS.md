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
| `GatewayPackageAotPublishTests.cs` | 普通打包五个 Kun 依赖包，独立 Linux NativeAOT 宿主通过 PackageReference 消费并验证 JWKS。 |
| `Fixtures/GatewayPackageConsumer/` | 复制到仓库外构建的消费者；不参加测试程序集编译，不允许引用 Kun 项目源码。 |
| `README.md` | 普通包消费回归的执行命令、隔离方式与验收边界。 |

## For AI Agents

### Working In This Directory

- **Do not add `ProjectReference` to `Kun.Sample.Minimal`.** We deliberately invoke the publish CLI so the test exercises exactly what a downstream consumer sees.
- Gateway 回归的库打包必须保持 `PublishAot=false` 与 `EnableRequestDelegateGenerator=false`，宿主只能使用新生成的 NuGet 包；不能让宿主的发布属性传入库并掩盖普通制品的运行时绑定问题。
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
