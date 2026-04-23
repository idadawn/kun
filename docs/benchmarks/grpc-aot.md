# gRPC AOT 基准与方法学

> Kun.Benchmarks.Grpc 用 BenchmarkDotNet 衡量 `Kun.Grpc` 热路径的成本，并给出 JIT / AOT 对比基线。本文档覆盖：方法论、复现脚本、预期指标、Part 2（AOT 矩阵）计划。
>
> 参考：`docs/architecture/why-cqrs.md` · 白皮书 §12.2 (P2-A)

## 为什么测这个

Kun.Grpc 的 `KunResultToRpcInterceptor` 在每次 gRPC 调用路径里都会执行，是"零开销抽象"的试金石：如果 `Result<T>` 模式 + 拦截器翻译引入的可测额外开销远小于 RPC 往返的网络延迟（微秒级 vs 毫秒级），那么这个设计是合格的；否则需要重新思考。

AOT 部分的价值是验证：

1. AOT 发布下拦截器路径**没有引入新的 trim/reflection 警告**（CI 用 `/warnaserror` 担保）。
2. AOT 与 JIT 的稳态延迟差异**在预期范围内**（AOT 通常略慢，但冷启动显著更快）。

## 当前提交（Part 1 scaffold）

这次交付仅做 **BenchmarkDotNet 项目骨架 + 一个 microbenchmark 类**，测 `ResultToRpcInterceptorBenchmarks` 的三种路径：

| Benchmark | 内容 |
|---|---|
| `NoInterceptor` (Baseline) | 直接调 handler 的 continuation，不走拦截器 |
| `WithSuccessResult` | 经拦截器，handler 返回 success，measure try/catch 框架开销 |
| `WithFailureResult` | 经拦截器，handler 抛 `ResultFailureException`，measure 翻译路径 |

运行：

```bash
dotnet run -c Release --project benchmarks/Kun.Benchmarks.Grpc -- --filter "*ResultToRpc*"
```

BenchmarkDotNet 会产出 `BenchmarkDotNet.Artifacts/results/` 目录，包含 `.html` / `.md` / `.csv` 报告。预期结果（.NET 10 JIT，Windows x64，单机本地跑）：

| Method | Mean | Ratio | Allocated |
|---|---|---|---|
| NoInterceptor | ~XX ns | 1.00 | 0 B |
| WithSuccessResult | ~YY ns | 1.0-1.5x | 0 B |
| WithFailureResult | ~ZZ µs | 10-100x | ~200 B (RpcException alloc) |

`WithFailureResult` 显著变慢是**预期的**——抛+捕获 `ResultFailureException`、分配 `RpcException` 是必须的成本。实际网络 RPC 往返在毫秒量级，所以微秒级的翻译开销完全可接受。

## Part 2（待做，P2-A）

Part 2 把 AOT 矩阵加上：

- 新增 CI workflow `.github/workflows/benchmarks.yml`（每周跑一次 + PR 标签触发）
- 把 `Kun.Benchmarks.Grpc` 的一个镜像项目改成 AOT-publish（`PublishAot=true`），单独构建运行，拿到 AOT 版本的数值
- 把 JIT 与 AOT 的 `Mean` / `Allocated` 写入同一张表格，放在本文档的 "Results" 节
- 目标：AOT 冷启动 < 100ms（独立测量），稳态延迟 AOT 不高于 JIT 的 1.5x

## 运行前的环境清单

- .NET 10 SDK 10.0.100+
- Release 构建（Debug 下 BenchmarkDotNet 会警告并退出）
- 关闭其他 CPU 重度任务，否则 noise 线会抬高
- Windows 上建议 `powercfg /setactive 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c`（High Performance 方案）

## 自定义

按 BenchmarkDotNet 惯例：
- 加 `[MemoryDiagnoser]` 已在 class 上
- 想加 threading: `[ThreadingDiagnoser]`
- 想加 native exception 跟踪: `[ExceptionDiagnoser]`
- 对比多个 .NET 版本：用 `[SimpleJob(RuntimeMoniker.Net80)]` + `[SimpleJob(RuntimeMoniker.Net100)]`

## 相关代码

- `benchmarks/Kun.Benchmarks.Grpc/Program.cs` — BenchmarkRunner entry
- `benchmarks/Kun.Benchmarks.Grpc/ResultToRpcInterceptorBenchmarks.cs` — 当前唯一 benchmark 类
- `src/Kun.Grpc/Interceptors/KunResultToRpcInterceptor.cs` — 被测路径
