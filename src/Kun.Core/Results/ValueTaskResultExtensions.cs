namespace Kun.Core.Results;

/// <summary>
/// Asynchronous railway-oriented extensions for
/// <see cref="ValueTask{TResult}"/> receivers whose result type is
/// <see cref="Result"/> or <see cref="Result{T}"/>. Follows ADR-011;
/// mirror of <see cref="TaskResultExtensions"/> for hot paths that
/// avoid <see cref="Task"/> allocation (Mediator pipeline, Saga step
/// executors).
/// </summary>
/// <remarks>
/// Return types and async-delegate return types are
/// <see cref="ValueTask"/> / <see cref="ValueTask{TResult}"/> to keep
/// the fluent chain on the ValueTask rail without
/// <c>.AsTask()</c> boundary friction. Callers holding
/// <see cref="Task{TResult}"/> sources should use
/// <see cref="TaskResultExtensions"/> instead.
/// </remarks>
public static class ValueTaskResultExtensions
{
    // ── ValueTask<Result> — non-generic ─────────────────────────────────────

    /// <summary>Awaits the source and invokes <paramref name="onSuccess"/> only when it succeeded.</summary>
    public static async ValueTask<Result> TapAsync(this ValueTask<Result> source, Action onSuccess)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        var result = await source.ConfigureAwait(false);
        return result.Tap(onSuccess);
    }

    /// <summary>Awaits the source and invokes an async side-effect on success, propagating <paramref name="ct"/>.</summary>
    public static async ValueTask<Result> TapAsync(
        this ValueTask<Result> source,
        Func<CancellationToken, ValueTask> onSuccess,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        var result = await source.ConfigureAwait(false);
        if (result.IsSuccess) await onSuccess(ct).ConfigureAwait(false);
        return result;
    }

    /// <summary>Awaits the source and applies a sync post-condition check.</summary>
    public static async ValueTask<Result> EnsureAsync(
        this ValueTask<Result> source,
        Func<bool> predicate,
        Error error)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(error);
        var result = await source.ConfigureAwait(false);
        return result.Ensure(predicate, error);
    }

    /// <summary>Awaits the source and applies an async post-condition check.</summary>
    public static async ValueTask<Result> EnsureAsync(
        this ValueTask<Result> source,
        Func<CancellationToken, ValueTask<bool>> predicate,
        Error error,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(error);
        var result = await source.ConfigureAwait(false);
        if (result.IsFailure) return result;
        var ok = await predicate(ct).ConfigureAwait(false);
        return ok ? result : Result.Fail(error);
    }

    /// <summary>Awaits the source and chains a sync follow-up returning <see cref="Result"/>.</summary>
    public static async ValueTask<Result> BindAsync(this ValueTask<Result> source, Func<Result> next)
    {
        ArgumentNullException.ThrowIfNull(next);
        var result = await source.ConfigureAwait(false);
        return result.Bind(next);
    }

    /// <summary>Awaits the source and chains an async follow-up returning <see cref="Result"/>.</summary>
    public static async ValueTask<Result> BindAsync(
        this ValueTask<Result> source,
        Func<CancellationToken, ValueTask<Result>> next,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(next);
        var result = await source.ConfigureAwait(false);
        return result.IsFailure ? result : await next(ct).ConfigureAwait(false);
    }

    /// <summary>Awaits the source and folds via sync delegates.</summary>
    public static async ValueTask<TOut> MatchAsync<TOut>(
        this ValueTask<Result> source,
        Func<TOut> onSuccess,
        Func<Error, TOut> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);
        var result = await source.ConfigureAwait(false);
        return result.Match(onSuccess, onFailure);
    }

    /// <summary>Awaits the source and folds via async delegates.</summary>
    public static async ValueTask<TOut> MatchAsync<TOut>(
        this ValueTask<Result> source,
        Func<CancellationToken, ValueTask<TOut>> onSuccess,
        Func<Error, CancellationToken, ValueTask<TOut>> onFailure,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);
        var result = await source.ConfigureAwait(false);
        return result.IsSuccess
            ? await onSuccess(ct).ConfigureAwait(false)
            : await onFailure(result.Error, ct).ConfigureAwait(false);
    }

    // ── ValueTask<Result<T>> ────────────────────────────────────────────────

    /// <summary>Awaits the source and applies a sync transform to its success value.</summary>
    public static async ValueTask<Result<TOut>> MapAsync<T, TOut>(
        this ValueTask<Result<T>> source,
        Func<T, TOut> map)
    {
        ArgumentNullException.ThrowIfNull(map);
        var result = await source.ConfigureAwait(false);
        return result.Map(map);
    }

    /// <summary>Awaits the source and applies an async transform to its success value.</summary>
    public static async ValueTask<Result<TOut>> MapAsync<T, TOut>(
        this ValueTask<Result<T>> source,
        Func<T, CancellationToken, ValueTask<TOut>> map,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(map);
        var result = await source.ConfigureAwait(false);
        if (result.IsFailure) return Result<TOut>.Fail(result.Error);
        var mapped = await map(result.Value, ct).ConfigureAwait(false);
        return Result<TOut>.Ok(mapped);
    }

    /// <summary>Awaits the source and chains a sync follow-up returning <see cref="Result{TOut}"/>.</summary>
    public static async ValueTask<Result<TOut>> BindAsync<T, TOut>(
        this ValueTask<Result<T>> source,
        Func<T, Result<TOut>> bind)
    {
        ArgumentNullException.ThrowIfNull(bind);
        var result = await source.ConfigureAwait(false);
        return result.Bind(bind);
    }

    /// <summary>Awaits the source and chains an async follow-up returning <see cref="Result{TOut}"/>.</summary>
    public static async ValueTask<Result<TOut>> BindAsync<T, TOut>(
        this ValueTask<Result<T>> source,
        Func<T, CancellationToken, ValueTask<Result<TOut>>> bind,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(bind);
        var result = await source.ConfigureAwait(false);
        return result.IsFailure
            ? Result<TOut>.Fail(result.Error)
            : await bind(result.Value, ct).ConfigureAwait(false);
    }

    /// <summary>Awaits the source and chains a sync follow-up returning non-generic <see cref="Result"/>.</summary>
    public static async ValueTask<Result> BindAsync<T>(
        this ValueTask<Result<T>> source,
        Func<T, Result> bind)
    {
        ArgumentNullException.ThrowIfNull(bind);
        var result = await source.ConfigureAwait(false);
        return result.Bind(bind);
    }

    /// <summary>Awaits the source and chains an async follow-up returning non-generic <see cref="Result"/>.</summary>
    public static async ValueTask<Result> BindAsync<T>(
        this ValueTask<Result<T>> source,
        Func<T, CancellationToken, ValueTask<Result>> bind,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(bind);
        var result = await source.ConfigureAwait(false);
        return result.IsFailure
            ? Result.Fail(result.Error)
            : await bind(result.Value, ct).ConfigureAwait(false);
    }

    /// <summary>Awaits the source and invokes a sync side-effect on success.</summary>
    public static async ValueTask<Result<T>> TapAsync<T>(
        this ValueTask<Result<T>> source,
        Action<T> onSuccess)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        var result = await source.ConfigureAwait(false);
        return result.Tap(onSuccess);
    }

    /// <summary>Awaits the source and invokes an async side-effect on success.</summary>
    public static async ValueTask<Result<T>> TapAsync<T>(
        this ValueTask<Result<T>> source,
        Func<T, CancellationToken, ValueTask> onSuccess,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        var result = await source.ConfigureAwait(false);
        if (result.IsSuccess) await onSuccess(result.Value, ct).ConfigureAwait(false);
        return result;
    }

    /// <summary>Awaits the source and applies a sync post-condition check.</summary>
    public static async ValueTask<Result<T>> EnsureAsync<T>(
        this ValueTask<Result<T>> source,
        Func<T, bool> predicate,
        Error error)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(error);
        var result = await source.ConfigureAwait(false);
        return result.Ensure(predicate, error);
    }

    /// <summary>Awaits the source and applies an async post-condition check.</summary>
    public static async ValueTask<Result<T>> EnsureAsync<T>(
        this ValueTask<Result<T>> source,
        Func<T, CancellationToken, ValueTask<bool>> predicate,
        Error error,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(error);
        var result = await source.ConfigureAwait(false);
        if (result.IsFailure) return result;
        var ok = await predicate(result.Value, ct).ConfigureAwait(false);
        return ok ? result : Result<T>.Fail(error);
    }

    /// <summary>Awaits the source and folds via sync delegates.</summary>
    public static async ValueTask<TOut> MatchAsync<T, TOut>(
        this ValueTask<Result<T>> source,
        Func<T, TOut> onSuccess,
        Func<Error, TOut> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);
        var result = await source.ConfigureAwait(false);
        return result.Match(onSuccess, onFailure);
    }

    /// <summary>Awaits the source and folds via async delegates.</summary>
    public static async ValueTask<TOut> MatchAsync<T, TOut>(
        this ValueTask<Result<T>> source,
        Func<T, CancellationToken, ValueTask<TOut>> onSuccess,
        Func<Error, CancellationToken, ValueTask<TOut>> onFailure,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);
        var result = await source.ConfigureAwait(false);
        return result.IsSuccess
            ? await onSuccess(result.Value, ct).ConfigureAwait(false)
            : await onFailure(result.Error, ct).ConfigureAwait(false);
    }
}
