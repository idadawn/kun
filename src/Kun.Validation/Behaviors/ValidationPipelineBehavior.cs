using Kun.Mediator;

namespace Kun.Validation.Behaviors;

/// <summary>
/// Pipeline behavior that runs every registered <see cref="IValidator{T}"/> against the
/// incoming request and throws <see cref="ValidationException"/> if any report errors.
/// <para>
/// For Result-based semantics (returning <c>Result.Fail(Error.Validation(...))</c> instead
/// of throwing), compose this behavior inside a user-written "ValidationResultAdapter"
/// behavior — shipped as a follow-up.
/// </para>
/// </summary>
/// <typeparam name="TRequest">The request type being validated.</typeparam>
/// <typeparam name="TResponse">The response type returned by the handler.</typeparam>
public sealed class ValidationPipelineBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : Kun.Core.Cqrs.IRequest<TResponse>
{
    private readonly IValidator<TRequest>[] _validators;

    /// <summary>
    /// Initializes a new instance of <see cref="ValidationPipelineBehavior{TRequest,TResponse}"/>.
    /// </summary>
    /// <param name="validators">All registered validators for <typeparamref name="TRequest"/>.</param>
    public ValidationPipelineBehavior(IEnumerable<IValidator<TRequest>> validators)
    {
        ArgumentNullException.ThrowIfNull(validators);
        _validators = validators.ToArray();
    }

    /// <inheritdoc/>
    public async ValueTask<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (_validators.Length == 0) return await next();

        var allErrors = new List<ValidationError>();
        for (var i = 0; i < _validators.Length; i++)
        {
            var result = await _validators[i].ValidateAsync(request, cancellationToken).ConfigureAwait(false);
            if (!result.IsValid)
            {
                for (var j = 0; j < result.Errors.Count; j++)
                    allErrors.Add(result.Errors[j]);
            }
        }

        if (allErrors.Count > 0)
            throw new ValidationException(allErrors);

        return await next();
    }
}
