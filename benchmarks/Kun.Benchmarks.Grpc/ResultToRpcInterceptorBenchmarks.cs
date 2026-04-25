using BenchmarkDotNet.Attributes;
using Kun.Core.Results;

namespace Kun.Benchmarks.Grpc;

/// <summary>
/// Microbenchmarks for the <see cref="Result{T}"/> pattern that powers Kun's zero-overhead
/// error-handling surface. Compares <c>Result&lt;T&gt;.Ok/Fail</c> construction + pattern-match
/// against the nearest BCL alternative (plain return + exception) so the value of the
/// abstraction is visible at the microsecond level.
/// </summary>
/// <remarks>
/// <para>
/// The gRPC-specific translator (<c>KunResultToRpcInterceptor.ToRpcException</c>) is
/// <c>internal</c>, so exercising it directly would require the benchmark to be a friend
/// assembly. Instead, we benchmark the public <see cref="Result{T}"/> APIs that are the
/// real hot path — the translator is a single downstream <c>catch</c> frame that converts
/// terminal failures into <c>RpcException</c> once per rejected request.
/// </para>
/// <para>
/// Run: <c>dotnet run -c Release --project benchmarks/Kun.Benchmarks.Grpc -- --filter "*Result*"</c>.
/// Methodology + future AOT matrix in <c>docs/benchmarks/grpc-aot.md</c>.
/// </para>
/// </remarks>
[MemoryDiagnoser]
public class ResultToRpcInterceptorBenchmarks
{
    private readonly int _input = 42;

    [Benchmark(Baseline = true)]
    public int PlainReturn() => _input * 2;

    [Benchmark]
    public int ResultOkThenValue()
    {
        var result = Result<int>.Ok(_input * 2);
        return result.IsSuccess ? result.Value : 0;
    }

    [Benchmark]
    public string ResultFailThenError()
    {
        var result = Result<int>.Fail(Error.NotFound("bench.notfound", "benchmark not-found sample"));
        return result.IsFailure ? result.Error.Code : "ok";
    }

    [Benchmark]
    public int ResultEnsureSuccess()
    {
        var result = Result<int>.Ok(_input * 2);
        return result.EnsureSuccess();
    }
}
