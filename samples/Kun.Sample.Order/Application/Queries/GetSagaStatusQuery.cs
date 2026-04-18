using Kun.Core.Cqrs;

namespace Kun.Sample.Order.Application.Queries;

/// <summary>Returns the current state of a saga instance by its ID.</summary>
public sealed record GetSagaStatusQuery(Guid SagaId) : IQuery<SagaStatusDto?>;

/// <summary>Read-side projection of a saga instance.</summary>
public sealed record SagaStatusDto(
    Guid Id,
    string SagaName,
    string State,
    int CurrentStepIndex,
    string? FailureReason,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt);
