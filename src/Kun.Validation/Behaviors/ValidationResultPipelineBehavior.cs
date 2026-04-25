using Kun.Core.Cqrs;
using Kun.Core.Results;
using Kun.Mediator;

namespace Kun.Validation.Behaviors;

/// <summary>
/// Non-generic variant of
/// <see cref="ValidationResultPipelineBehavior{TRequest, TValue}"/> for
/// handlers whose response is the non-generic <see cref="Result"/>
/// (void commands with no return value). Returns
/// <c>Result.Fail(Error.Validation("request.invalid", …))</c>
/// instead of throwing when validators report errors. Closes ADR-014
/// §F1 follow-up.
/// </summary>
/// <typeparam name="TRequest">The request type being validated.</typeparam>
public sealed class ValidationResultPipelineBehavior<TRequest> : IPipelineBehavior<TRequest, Result>
    where TRequest : IRequest<Result>
{
    private readonly IValidator<TRequest>[] _validators;

    /// <summary>
    /// Initializes a new instance of
    /// <see cref="ValidationResultPipelineBehavior{TRequest}"/>.
    /// </summary>
    /// <param name="validators">All registered validators for <typeparamref name="TRequest"/>.</param>
    public ValidationResultPipelineBehavior(IEnumerable<IValidator<TRequest>> validators)
    {
        ArgumentNullException.ThrowIfNull(validators);
        _validators = validators.ToArray();
    }

    /// <inheritdoc/>
    public async ValueTask<Result> Handle(
        TRequest request,
        RequestHandlerDelegate<Result> next,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);
        if (_validators.Length == 0) return await next().ConfigureAwait(false);

        List<ValidationError>? allErrors = null;
        for (var i = 0; i < _validators.Length; i++)
        {
            var result = await _validators[i]
                .ValidateAsync(request, cancellationToken)
                .ConfigureAwait(false);
            if (result.IsValid) continue;

            allErrors ??= [];
            for (var j = 0; j < result.Errors.Count; j++)
                allErrors.Add(result.Errors[j]);
        }

        if (allErrors is { Count: > 0 })
        {
            var message = string.Join("; ", allErrors.Select(e => e.Message));
            return Result.Fail(Error.Validation("request.invalid", message));
        }

        return await next().ConfigureAwait(false);
    }
}

/// <summary>
/// Pipeline behavior that runs every registered <see cref="IValidator{T}"/>
/// against the incoming request and returns
/// <c>Result&lt;TValue&gt;.Fail(Error.Validation("request.invalid", …))</c>
/// instead of throwing when any report errors. Companion to the
/// exception-throwing <see cref="ValidationPipelineBehavior{TRequest, TResponse}"/>
/// for handlers that deliberately compose via <see cref="Result{T}"/>
/// and want validation failures to surface through the normal
/// railway pipeline (see <c>docs/architecture/ADR-011-Result-async-extensions.md</c>
/// and <c>docs/architecture/ADR-014-Validation-Result-behavior.md</c>).
/// </summary>
/// <remarks>
/// Register per request type via
/// <c>KunMediatorBuilder.AddValidationResultBehavior&lt;TRequest, TValue&gt;()</c>;
/// do **not** register alongside <see cref="ValidationPipelineBehavior{TRequest, TResponse}"/>
/// for the same request — pick one.
/// </remarks>
/// <typeparam name="TRequest">The request type being validated.</typeparam>
/// <typeparam name="TValue">The success value carried by the handler's <see cref="Result{TValue}"/>.</typeparam>
public sealed class ValidationResultPipelineBehavior<TRequest, TValue> : IPipelineBehavior<TRequest, Result<TValue>>
    where TRequest : IRequest<Result<TValue>>
{
    private readonly IValidator<TRequest>[] _validators;

    /// <summary>
    /// Initializes a new instance of
    /// <see cref="ValidationResultPipelineBehavior{TRequest, TValue}"/>.
    /// </summary>
    /// <param name="validators">All registered validators for <typeparamref name="TRequest"/>.</param>
    public ValidationResultPipelineBehavior(IEnumerable<IValidator<TRequest>> validators)
    {
        ArgumentNullException.ThrowIfNull(validators);
        _validators = validators.ToArray();
    }

    /// <inheritdoc/>
    public async ValueTask<Result<TValue>> Handle(
        TRequest request,
        RequestHandlerDelegate<Result<TValue>> next,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);
        if (_validators.Length == 0) return await next().ConfigureAwait(false);

        List<ValidationError>? allErrors = null;
        for (var i = 0; i < _validators.Length; i++)
        {
            var result = await _validators[i]
                .ValidateAsync(request, cancellationToken)
                .ConfigureAwait(false);
            if (result.IsValid) continue;

            allErrors ??= [];
            for (var j = 0; j < result.Errors.Count; j++)
                allErrors.Add(result.Errors[j]);
        }

        if (allErrors is { Count: > 0 })
        {
            var message = string.Join("; ", allErrors.Select(e => e.Message));
            return Result<TValue>.Fail(Error.Validation("request.invalid", message));
        }

        return await next().ConfigureAwait(false);
    }
}
