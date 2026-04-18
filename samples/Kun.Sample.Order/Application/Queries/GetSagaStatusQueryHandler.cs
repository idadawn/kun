using Kun.Core.Cqrs;
using Kun.Saga.Persistence;

namespace Kun.Sample.Order.Application.Queries;

/// <summary>Handles <see cref="GetSagaStatusQuery"/> using <see cref="ISagaInstanceStore"/>.</summary>
public sealed class GetSagaStatusQueryHandler : IRequestHandler<GetSagaStatusQuery, SagaStatusDto?>
{
    private readonly ISagaInstanceStore _store;

    /// <summary>Creates a new <see cref="GetSagaStatusQueryHandler"/>.</summary>
    public GetSagaStatusQueryHandler(ISagaInstanceStore store) => _store = store;

    /// <inheritdoc/>
    public async ValueTask<SagaStatusDto?> Handle(GetSagaStatusQuery query, CancellationToken cancellationToken)
    {
        var instance = await _store.FindAsync(query.SagaId, cancellationToken);
        return instance is null ? null : new SagaStatusDto(
            Id: instance.Id,
            SagaName: instance.SagaName,
            State: instance.State.ToString(),
            CurrentStepIndex: instance.CurrentStepIndex,
            FailureReason: instance.FailureReason,
            StartedAt: instance.StartedAt,
            CompletedAt: instance.CompletedAt);
    }
}
