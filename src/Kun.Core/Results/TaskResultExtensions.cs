namespace Kun.Core.Results;

/// <summary>
/// Asynchronous railway-oriented extensions for <see cref="Task{TResult}"/>
/// receivers whose result type is <see cref="Result"/> or
/// <see cref="Result{T}"/>. Follows ADR-011.
/// </summary>
/// <remarks>
/// Each operation provides two delegate-shape overloads:
/// <list type="bullet">
///   <item>a <i>sync-delegate</i> variant (no <see cref="CancellationToken"/>)</item>
///   <item>an <i>async-delegate</i> variant that accepts a
///         <see cref="CancellationToken"/> and forwards it to the delegate.</item>
/// </list>
/// Failure inputs short-circuit without invoking the delegate. Faulted or
/// cancelled source tasks surface through <c>await</c> per normal
/// .NET semantics — neither a <see cref="Result"/> failure nor a wrapped
/// exception.
/// </remarks>
public static class TaskResultExtensions
{
    // ── Task<Result> — non-generic ──────────────────────────────────────────

    /// <summary>Awaits the source and invokes <paramref name="onSuccess"/> only when it succeeded.</summary>
    public static async Task<Result> TapAsync(this Task<Result> source, Action onSuccess)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(onSuccess);
        var result = await source.ConfigureAwait(false);
        return result.Tap(onSuccess);
    }

    /// <summary>Awaits the source and invokes <paramref name="onSuccess"/> only when it succeeded, propagating <paramref name="ct"/>.</summary>
    public static async Task<Result> TapAsync(
        this Task<Result> source,
        Func<CancellationToken, Task> onSuccess,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(onSuccess);
        var result = await source.ConfigureAwait(false);
        if (result.IsSuccess) await onSuccess(ct).ConfigureAwait(false);
        return result;
    }

    /// <summary>Awaits the source and applies an <see cref="ResultExtensions.Ensure(Result, Func{bool}, Error)"/> check.</summary>
    public static async Task<Result> EnsureAsync(
        this Task<Result> source,
        Func<bool> predicate,
        Error error)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(error);
        var result = await source.ConfigureAwait(false);
        return result.Ensure(predicate, error);
    }

    /// <summary>Awaits the source and applies an async predicate post-condition.</summary>
    public static async Task<Result> EnsureAsync(
        this Task<Result> source,
        Func<CancellationToken, Task<bool>> predicate,
        Error error,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(error);
        var result = await source.ConfigureAwait(false);
        if (result.IsFailure) return result;
        var ok = await predicate(ct).ConfigureAwait(false);
        return ok ? result : Result.Fail(error);
    }

    /// <summary>Awaits the source and chains a sync follow-up that itself returns <see cref="Result"/>.</summary>
    public static async Task<Result> BindAsync(this Task<Result> source, Func<Result> next)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(next);
        var result = await source.ConfigureAwait(false);
        return result.Bind(next);
    }

    /// <summary>Awaits the source and chains an async follow-up returning <see cref="Result"/>.</summary>
    public static async Task<Result> BindAsync(
        this Task<Result> source,
        Func<CancellationToken, Task<Result>> next,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(next);
        var result = await source.ConfigureAwait(false);
        return result.IsFailure ? result : await next(ct).ConfigureAwait(false);
    }

    /// <summary>Awaits the source and folds via sync delegates.</summary>
    public static async Task<TOut> MatchAsync<TOut>(
        this Task<Result> source,
        Func<TOut> onSuccess,
        Func<Error, TOut> onFailure)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);
        var result = await source.ConfigureAwait(false);
        return result.Match(onSuccess, onFailure);
    }

    /// <summary>Awaits the source and folds via async delegates, propagating <paramref name="ct"/>.</summary>
    public static async Task<TOut> MatchAsync<TOut>(
        this Task<Result> source,
        Func<CancellationToken, Task<TOut>> onSuccess,
        Func<Error, CancellationToken, Task<TOut>> onFailure,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);
        var result = await source.ConfigureAwait(false);
        return result.IsSuccess
            ? await onSuccess(ct).ConfigureAwait(false)
            : await onFailure(result.Error, ct).ConfigureAwait(false);
    }

    // ── Task<Result<T>> ─────────────────────────────────────────────────────

    /// <summary>Awaits the source and applies a sync transform to its success value.</summary>
    public static async Task<Result<TOut>> MapAsync<T, TOut>(
        this Task<Result<T>> source,
        Func<T, TOut> map)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(map);
        var result = await source.ConfigureAwait(false);
        return result.Map(map);
    }

    /// <summary>Awaits the source and applies an async transform to its success value.</summary>
    public static async Task<Result<TOut>> MapAsync<T, TOut>(
        this Task<Result<T>> source,
        Func<T, CancellationToken, Task<TOut>> map,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(map);
        var result = await source.ConfigureAwait(false);
        if (result.IsFailure) return Result<TOut>.Fail(result.Error);
        var mapped = await map(result.Value, ct).ConfigureAwait(false);
        return Result<TOut>.Ok(mapped);
    }

    /// <summary>Awaits the source and chains a sync follow-up returning <see cref="Result{TOut}"/>.</summary>
    public static async Task<Result<TOut>> BindAsync<T, TOut>(
        this Task<Result<T>> source,
        Func<T, Result<TOut>> bind)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(bind);
        var result = await source.ConfigureAwait(false);
        return result.Bind(bind);
    }

    /// <summary>Awaits the source and chains an async follow-up returning <see cref="Result{TOut}"/>.</summary>
    public static async Task<Result<TOut>> BindAsync<T, TOut>(
        this Task<Result<T>> source,
        Func<T, CancellationToken, Task<Result<TOut>>> bind,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(bind);
        var result = await source.ConfigureAwait(false);
        return result.IsFailure
            ? Result<TOut>.Fail(result.Error)
            : await bind(result.Value, ct).ConfigureAwait(false);
    }

    /// <summary>Awaits the source and chains a sync follow-up returning non-generic <see cref="Result"/>.</summary>
    public static async Task<Result> BindAsync<T>(
        this Task<Result<T>> source,
        Func<T, Result> bind)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(bind);
        var result = await source.ConfigureAwait(false);
        return result.Bind(bind);
    }

    /// <summary>Awaits the source and chains an async follow-up returning non-generic <see cref="Result"/>.</summary>
    public static async Task<Result> BindAsync<T>(
        this Task<Result<T>> source,
        Func<T, CancellationToken, Task<Result>> bind,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(bind);
        var result = await source.ConfigureAwait(false);
        return result.IsFailure
            ? Result.Fail(result.Error)
            : await bind(result.Value, ct).ConfigureAwait(false);
    }

    /// <summary>Awaits the source and invokes a sync side-effect on success.</summary>
    public static async Task<Result<T>> TapAsync<T>(
        this Task<Result<T>> source,
        Action<T> onSuccess)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(onSuccess);
        var result = await source.ConfigureAwait(false);
        return result.Tap(onSuccess);
    }

    /// <summary>Awaits the source and invokes an async side-effect on success.</summary>
    public static async Task<Result<T>> TapAsync<T>(
        this Task<Result<T>> source,
        Func<T, CancellationToken, Task> onSuccess,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(onSuccess);
        var result = await source.ConfigureAwait(false);
        if (result.IsSuccess) await onSuccess(result.Value, ct).ConfigureAwait(false);
        return result;
    }

    /// <summary>Awaits the source and applies a sync post-condition check.</summary>
    public static async Task<Result<T>> EnsureAsync<T>(
        this Task<Result<T>> source,
        Func<T, bool> predicate,
        Error error)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(error);
        var result = await source.ConfigureAwait(false);
        return result.Ensure(predicate, error);
    }

    /// <summary>Awaits the source and applies an async post-condition check.</summary>
    public static async Task<Result<T>> EnsureAsync<T>(
        this Task<Result<T>> source,
        Func<T, CancellationToken, Task<bool>> predicate,
        Error error,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(error);
        var result = await source.ConfigureAwait(false);
        if (result.IsFailure) return result;
        var ok = await predicate(result.Value, ct).ConfigureAwait(false);
        return ok ? result : Result<T>.Fail(error);
    }

    /// <summary>Awaits the source and folds via sync delegates.</summary>
    public static async Task<TOut> MatchAsync<T, TOut>(
        this Task<Result<T>> source,
        Func<T, TOut> onSuccess,
        Func<Error, TOut> onFailure)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);
        var result = await source.ConfigureAwait(false);
        return result.Match(onSuccess, onFailure);
    }

    /// <summary>Awaits the source and folds via async delegates, propagating <paramref name="ct"/>.</summary>
    public static async Task<TOut> MatchAsync<T, TOut>(
        this Task<Result<T>> source,
        Func<T, CancellationToken, Task<TOut>> onSuccess,
        Func<Error, CancellationToken, Task<TOut>> onFailure,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);
        var result = await source.ConfigureAwait(false);
        return result.IsSuccess
            ? await onSuccess(result.Value, ct).ConfigureAwait(false)
            : await onFailure(result.Error, ct).ConfigureAwait(false);
    }
}
