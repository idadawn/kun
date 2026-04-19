using Kun.Mediator;

namespace Kun.EntityFrameworkCore.Behaviors;

/// <summary>
/// Mediator pipeline behavior that commits the unit-of-work after a command handler returns
/// successfully. Place this behavior after validation but before logging so the commit
/// participates in the correct observability scope.
/// </summary>
/// <typeparam name="TRequest">Command type.</typeparam>
/// <typeparam name="TResponse">Handler response type.</typeparam>
public sealed class UnitOfWorkPipelineBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : Kun.Core.Cqrs.IRequest<TResponse>
{
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Creates a new <see cref="UnitOfWorkPipelineBehavior{TRequest, TResponse}"/>.</summary>
    public UnitOfWorkPipelineBehavior(IUnitOfWork unitOfWork)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc/>
    public async ValueTask<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var response = await next().ConfigureAwait(false);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return response;
    }
}
