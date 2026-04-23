using FluentAssertions;
using Kun.Core.Results;
using Xunit;

namespace Kun.Core.Tests.Results;

public class ResultExtensionsTests
{
    private static readonly Error TestError = Error.Failure("test.fail", "test failure");
    private static readonly Error OtherError = Error.Validation("test.bad", "validation failed");

    // ── Sync — Result<T> ────────────────────────────────────────────────────

    [Fact]
    public void Map_OnSuccess_TransformsValue()
    {
        var r = Result<int>.Ok(3).Map(x => x * 2);
        r.IsSuccess.Should().BeTrue();
        r.Value.Should().Be(6);
    }

    [Fact]
    public void Map_OnFailure_ShortCircuitsWithoutInvokingDelegate()
    {
        var called = false;
        var r = Result<int>.Fail(TestError).Map(x => { called = true; return x * 2; });
        r.IsFailure.Should().BeTrue();
        r.Error.Should().Be(TestError);
        called.Should().BeFalse();
    }

    [Fact]
    public void Bind_OnSuccess_InvokesDelegateAndReturnsItsResult()
    {
        var r = Result<int>.Ok(3).Bind(x => Result<string>.Ok($"v={x}"));
        r.Value.Should().Be("v=3");
    }

    [Fact]
    public void Bind_OnSuccessReturningFailure_CarriesDelegateFailure()
    {
        var r = Result<int>.Ok(3).Bind(_ => Result<string>.Fail(OtherError));
        r.IsFailure.Should().BeTrue();
        r.Error.Should().Be(OtherError);
    }

    [Fact]
    public void Bind_OnFailure_ShortCircuits()
    {
        var called = false;
        var r = Result<int>.Fail(TestError).Bind(_ => { called = true; return Result<string>.Ok("x"); });
        r.Error.Should().Be(TestError);
        called.Should().BeFalse();
    }

    [Fact]
    public void Bind_ToNonGenericResult_OnSuccess_InvokesDelegate()
    {
        var called = false;
        var r = Result<int>.Ok(3).Bind(_ => { called = true; return Result.Ok(); });
        r.IsSuccess.Should().BeTrue();
        called.Should().BeTrue();
    }

    [Fact]
    public void Tap_OnSuccess_InvokesDelegateButReturnsOriginal()
    {
        var seen = 0;
        var r = Result<int>.Ok(42).Tap(x => seen = x);
        r.Value.Should().Be(42);
        seen.Should().Be(42);
    }

    [Fact]
    public void Tap_OnFailure_DoesNotInvokeDelegate()
    {
        var called = false;
        var r = Result<int>.Fail(TestError).Tap(_ => called = true);
        r.Error.Should().Be(TestError);
        called.Should().BeFalse();
    }

    [Fact]
    public void Ensure_OnSuccessPredicateTrue_PassesThrough()
    {
        var r = Result<int>.Ok(3).Ensure(x => x > 0, OtherError);
        r.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Ensure_OnSuccessPredicateFalse_BecomesFailureWithSuppliedError()
    {
        var r = Result<int>.Ok(-1).Ensure(x => x > 0, OtherError);
        r.IsFailure.Should().BeTrue();
        r.Error.Should().Be(OtherError);
    }

    [Fact]
    public void Ensure_OnFailure_PreservesOriginalErrorAndSkipsPredicate()
    {
        var called = false;
        var r = Result<int>.Fail(TestError).Ensure(_ => { called = true; return true; }, OtherError);
        r.Error.Should().Be(TestError);
        called.Should().BeFalse();
    }

    [Fact]
    public void Match_OnSuccess_InvokesOnSuccessOnly()
    {
        var which = "";
        var r = Result<int>.Ok(5).Match(x => which = $"ok:{x}", _ => which = "fail");
        which.Should().Be("ok:5");
        r.Should().Be("ok:5");
    }

    [Fact]
    public void Match_OnFailure_InvokesOnFailureOnly()
    {
        var which = "";
        var r = Result<int>.Fail(TestError).Match(_ => which = "ok", e => which = $"fail:{e.Code}");
        which.Should().Be("fail:test.fail");
        r.Should().Be("fail:test.fail");
    }

    // ── Sync — non-generic Result ───────────────────────────────────────────

    [Fact]
    public void NonGeneric_Bind_Chain_SuccessThenSuccess()
    {
        var r = Result.Ok().Bind(() => Result.Ok());
        r.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void NonGeneric_Bind_OnFailure_ShortCircuits()
    {
        var called = false;
        var r = Result.Fail(TestError).Bind(() => { called = true; return Result.Ok(); });
        r.Error.Should().Be(TestError);
        called.Should().BeFalse();
    }

    [Fact]
    public void NonGeneric_Ensure_OnSuccessFalsePredicate_BecomesFailure()
    {
        var r = Result.Ok().Ensure(() => false, OtherError);
        r.Error.Should().Be(OtherError);
    }

    // ── Async — Task<Result<T>> source ──────────────────────────────────────

    [Fact]
    public async Task MapAsync_Task_SyncDelegate_TransformsValue()
    {
        var r = await Task.FromResult(Result<int>.Ok(3)).MapAsync(x => x + 1);
        r.Value.Should().Be(4);
    }

    [Fact]
    public async Task MapAsync_Task_AsyncDelegate_TransformsValueAndForwardsCt()
    {
        using var cts = new CancellationTokenSource();
        CancellationToken seen = default;
        var r = await Task.FromResult(Result<int>.Ok(3))
            .MapAsync((x, ct) => { seen = ct; return Task.FromResult(x + 1); }, cts.Token);
        r.Value.Should().Be(4);
        seen.Should().Be(cts.Token);
    }

    [Fact]
    public async Task MapAsync_Task_OnFailure_ShortCircuitsAsyncDelegate()
    {
        var called = false;
        var r = await Task.FromResult(Result<int>.Fail(TestError))
            .MapAsync((_, _) => { called = true; return Task.FromResult(0); }, CancellationToken.None);
        r.Error.Should().Be(TestError);
        called.Should().BeFalse();
    }

    [Fact]
    public async Task MapAsync_Task_PreCancelledCt_SurfacesOperationCanceledNotResult()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var act = async () => await Task.FromResult(Result<int>.Ok(3))
            .MapAsync(async (x, ct) => { ct.ThrowIfCancellationRequested(); return x + 1; }, cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task BindAsync_Task_AsyncDelegate_OnSuccess_ChainsResult()
    {
        var r = await Task.FromResult(Result<int>.Ok(3))
            .BindAsync((x, _) => Task.FromResult(Result<string>.Ok($"v={x}")), CancellationToken.None);
        r.Value.Should().Be("v=3");
    }

    [Fact]
    public async Task BindAsync_Task_ToNonGeneric_OnSuccess_Chains()
    {
        var r = await Task.FromResult(Result<int>.Ok(3))
            .BindAsync(_ => Result.Ok());
        r.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task TapAsync_Task_AsyncDelegate_OnSuccess_InvokesWithCt()
    {
        using var cts = new CancellationTokenSource();
        CancellationToken seen = default;
        var r = await Task.FromResult(Result<int>.Ok(3))
            .TapAsync((_, ct) => { seen = ct; return Task.CompletedTask; }, cts.Token);
        r.Value.Should().Be(3);
        seen.Should().Be(cts.Token);
    }

    [Fact]
    public async Task TapAsync_Task_AsyncDelegate_OnFailure_DoesNotInvoke()
    {
        var called = false;
        await Task.FromResult(Result<int>.Fail(TestError))
            .TapAsync((_, _) => { called = true; return Task.CompletedTask; }, CancellationToken.None);
        called.Should().BeFalse();
    }

    [Fact]
    public async Task EnsureAsync_Task_AsyncPredicateFalse_BecomesFailure()
    {
        var r = await Task.FromResult(Result<int>.Ok(3))
            .EnsureAsync((_, _) => Task.FromResult(false), OtherError, CancellationToken.None);
        r.Error.Should().Be(OtherError);
    }

    [Fact]
    public async Task EnsureAsync_Task_OnFailure_SkipsPredicateAndPreservesError()
    {
        var called = false;
        var r = await Task.FromResult(Result<int>.Fail(TestError))
            .EnsureAsync((_, _) => { called = true; return Task.FromResult(true); }, OtherError, CancellationToken.None);
        r.Error.Should().Be(TestError);
        called.Should().BeFalse();
    }

    [Fact]
    public async Task MatchAsync_Task_AsyncDelegates_OnSuccess_InvokesOnSuccessAndForwardsCt()
    {
        using var cts = new CancellationTokenSource();
        CancellationToken seen = default;
        var r = await Task.FromResult(Result<int>.Ok(3))
            .MatchAsync(
                (x, ct) => { seen = ct; return Task.FromResult(x * 10); },
                (_, _) => Task.FromResult(-1),
                cts.Token);
        r.Should().Be(30);
        seen.Should().Be(cts.Token);
    }

    [Fact]
    public async Task MatchAsync_Task_AsyncDelegates_OnFailure_InvokesOnFailureWithCt()
    {
        using var cts = new CancellationTokenSource();
        CancellationToken seen = default;
        var r = await Task.FromResult(Result<int>.Fail(TestError))
            .MatchAsync(
                (_, _) => Task.FromResult(0),
                (e, ct) => { seen = ct; return Task.FromResult(e.Code.Length); },
                cts.Token);
        r.Should().Be(TestError.Code.Length);
        seen.Should().Be(cts.Token);
    }

    // ── Async — Task<Result> (non-generic) source ───────────────────────────

    [Fact]
    public async Task NonGeneric_BindAsync_Task_AsyncDelegate_OnSuccess_Chains()
    {
        var r = await Task.FromResult(Result.Ok())
            .BindAsync(_ => Task.FromResult(Result.Ok()), CancellationToken.None);
        r.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task NonGeneric_MatchAsync_Task_SyncDelegates_FoldsToValue()
    {
        var r = await Task.FromResult(Result.Ok()).MatchAsync(() => "ok", _ => "fail");
        r.Should().Be("ok");
    }

    // ── Async — ValueTask<Result<T>> source ─────────────────────────────────

    [Fact]
    public async Task MapAsync_ValueTask_SyncDelegate_TransformsValue()
    {
        var r = await new ValueTask<Result<int>>(Result<int>.Ok(3)).MapAsync(x => x + 1);
        r.Value.Should().Be(4);
    }

    [Fact]
    public async Task MapAsync_ValueTask_AsyncDelegate_TransformsValueAndForwardsCt()
    {
        using var cts = new CancellationTokenSource();
        CancellationToken seen = default;
        var r = await new ValueTask<Result<int>>(Result<int>.Ok(3))
            .MapAsync((x, ct) => { seen = ct; return new ValueTask<int>(x + 1); }, cts.Token);
        r.Value.Should().Be(4);
        seen.Should().Be(cts.Token);
    }

    [Fact]
    public async Task MapAsync_ValueTask_OnFailure_ShortCircuits()
    {
        var called = false;
        var r = await new ValueTask<Result<int>>(Result<int>.Fail(TestError))
            .MapAsync((_, _) => { called = true; return new ValueTask<int>(0); }, CancellationToken.None);
        r.Error.Should().Be(TestError);
        called.Should().BeFalse();
    }

    [Fact]
    public async Task BindAsync_ValueTask_AsyncDelegate_OnSuccess_Chains()
    {
        var r = await new ValueTask<Result<int>>(Result<int>.Ok(3))
            .BindAsync((x, _) => new ValueTask<Result<string>>(Result<string>.Ok($"v={x}")), CancellationToken.None);
        r.Value.Should().Be("v=3");
    }

    [Fact]
    public async Task EnsureAsync_ValueTask_AsyncPredicateFalse_BecomesFailure()
    {
        var r = await new ValueTask<Result<int>>(Result<int>.Ok(3))
            .EnsureAsync((_, _) => new ValueTask<bool>(false), OtherError, CancellationToken.None);
        r.Error.Should().Be(OtherError);
    }

    [Fact]
    public async Task TapAsync_ValueTask_AsyncDelegate_OnSuccess_Invokes()
    {
        var seen = 0;
        var r = await new ValueTask<Result<int>>(Result<int>.Ok(7))
            .TapAsync((x, _) => { seen = x; return ValueTask.CompletedTask; }, CancellationToken.None);
        seen.Should().Be(7);
        r.Value.Should().Be(7);
    }

    [Fact]
    public async Task MatchAsync_ValueTask_AsyncDelegates_OnSuccess_Folds()
    {
        var r = await new ValueTask<Result<int>>(Result<int>.Ok(4))
            .MatchAsync(
                (x, _) => new ValueTask<int>(x * x),
                (_, _) => new ValueTask<int>(-1),
                CancellationToken.None);
        r.Should().Be(16);
    }

    // ── Task / ValueTask parity: same source value produces same outcome ────

    [Fact]
    public async Task Parity_MapAsync_TaskAndValueTask_YieldSameResult()
    {
        var taskResult = await Task.FromResult(Result<int>.Ok(5)).MapAsync(x => x * 2);
        var valueTaskResult = await new ValueTask<Result<int>>(Result<int>.Ok(5)).MapAsync(x => x * 2);
        taskResult.Value.Should().Be(valueTaskResult.Value);
    }

    [Fact]
    public async Task Parity_BindAsync_OnFailure_TaskAndValueTaskBothPreserveError()
    {
        var taskResult = await Task.FromResult(Result<int>.Fail(TestError)).BindAsync(x => Result<string>.Ok($"v={x}"));
        var valueTaskResult = await new ValueTask<Result<int>>(Result<int>.Fail(TestError))
            .BindAsync(x => Result<string>.Ok($"v={x}"));
        taskResult.Error.Should().Be(TestError);
        valueTaskResult.Error.Should().Be(TestError);
    }

    // ── Null-argument guards ────────────────────────────────────────────────

    [Fact]
    public void Map_NullDelegate_Throws()
    {
        var act = () => Result<int>.Ok(1).Map<int, int>(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task MapAsync_Task_NullAsyncDelegate_Throws()
    {
        var act = async () => await Task.FromResult(Result<int>.Ok(1))
            .MapAsync<int, int>((Func<int, CancellationToken, Task<int>>)null!, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
