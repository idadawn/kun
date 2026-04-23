namespace Kun.Core.Results;

/// <summary>
/// Synchronous railway-oriented extensions for <see cref="Result"/> and
/// <see cref="Result{T}"/>. Follows ADR-011.
/// </summary>
/// <remarks>
/// Every method short-circuits on failure inputs: the supplied delegate is
/// <b>not</b> invoked and the original <see cref="Error"/> is carried through
/// unchanged. See <c>docs/architecture/ADR-011-Result-async-extensions.md</c>
/// for normative semantics, naming rationale, and async counterparts on
/// <c>Task&lt;Result&gt;</c> / <c>ValueTask&lt;Result&gt;</c> sources.
/// </remarks>
public static class ResultExtensions
{
    // ── Result (non-generic) ────────────────────────────────────────────────

    /// <summary>
    /// Invokes <paramref name="onSuccess"/> only when the result is
    /// successful; returns the input result unchanged. Common use: side
    /// effects such as logging or metric increments that must not mutate
    /// the pipeline value.
    /// </summary>
    public static Result Tap(this Result result, Action onSuccess)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        if (result.IsSuccess) onSuccess();
        return result;
    }

    /// <summary>
    /// Adds a post-condition check to a successful result. When
    /// <paramref name="predicate"/> returns <see langword="false"/>, the
    /// result becomes a failure carrying <paramref name="error"/>. Failure
    /// inputs short-circuit and are returned unchanged.
    /// </summary>
    public static Result Ensure(this Result result, Func<bool> predicate, Error error)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(error);
        if (result.IsFailure) return result;
        return predicate() ? result : Result.Fail(error);
    }

    /// <summary>
    /// Chains a follow-up step that itself returns a <see cref="Result"/>.
    /// If the input is failed, the supplied delegate is not invoked and
    /// the original error is returned.
    /// </summary>
    public static Result Bind(this Result result, Func<Result> next)
    {
        ArgumentNullException.ThrowIfNull(next);
        return result.IsFailure ? result : next();
    }

    /// <summary>
    /// Terminal fold. Invokes exactly one of <paramref name="onSuccess"/> or
    /// <paramref name="onFailure"/> depending on the result state and
    /// returns the produced value.
    /// </summary>
    public static TOut Match<TOut>(
        this Result result,
        Func<TOut> onSuccess,
        Func<Error, TOut> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);
        return result.IsSuccess ? onSuccess() : onFailure(result.Error);
    }

    // ── Result<T> ───────────────────────────────────────────────────────────

    /// <summary>
    /// Transforms the success value via <paramref name="map"/>. Failure
    /// inputs short-circuit and the original <see cref="Error"/> is
    /// preserved in the returned <see cref="Result{TOut}"/>.
    /// </summary>
    public static Result<TOut> Map<T, TOut>(this Result<T> result, Func<T, TOut> map)
    {
        ArgumentNullException.ThrowIfNull(map);
        return result.IsSuccess
            ? Result<TOut>.Ok(map(result.Value))
            : Result<TOut>.Fail(result.Error);
    }

    /// <summary>
    /// Chains a follow-up step whose output is itself a
    /// <see cref="Result{TOut}"/>. Failure inputs short-circuit; the
    /// supplied delegate only runs on success.
    /// </summary>
    public static Result<TOut> Bind<T, TOut>(this Result<T> result, Func<T, Result<TOut>> bind)
    {
        ArgumentNullException.ThrowIfNull(bind);
        return result.IsSuccess ? bind(result.Value) : Result<TOut>.Fail(result.Error);
    }

    /// <summary>
    /// Chains a follow-up step whose output is a non-generic
    /// <see cref="Result"/>. Useful for "do this side-effectful op next"
    /// that doesn't produce a new value.
    /// </summary>
    public static Result Bind<T>(this Result<T> result, Func<T, Result> bind)
    {
        ArgumentNullException.ThrowIfNull(bind);
        return result.IsSuccess ? bind(result.Value) : Result.Fail(result.Error);
    }

    /// <summary>
    /// Invokes <paramref name="onSuccess"/> with the success value. Returns
    /// the input result unchanged; failures short-circuit without calling
    /// the delegate.
    /// </summary>
    public static Result<T> Tap<T>(this Result<T> result, Action<T> onSuccess)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        if (result.IsSuccess) onSuccess(result.Value);
        return result;
    }

    /// <summary>
    /// Adds a post-condition check on a successful <see cref="Result{T}"/>.
    /// When <paramref name="predicate"/> returns <see langword="false"/>,
    /// the result becomes a failure carrying <paramref name="error"/>.
    /// </summary>
    public static Result<T> Ensure<T>(this Result<T> result, Func<T, bool> predicate, Error error)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(error);
        if (result.IsFailure) return result;
        return predicate(result.Value) ? result : Result<T>.Fail(error);
    }

    /// <summary>
    /// Terminal fold. Invokes exactly one of <paramref name="onSuccess"/>
    /// (with the success value) or <paramref name="onFailure"/> (with the
    /// carried <see cref="Error"/>) and returns the produced value.
    /// </summary>
    public static TOut Match<T, TOut>(
        this Result<T> result,
        Func<T, TOut> onSuccess,
        Func<Error, TOut> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);
        return result.IsSuccess ? onSuccess(result.Value) : onFailure(result.Error);
    }
}
