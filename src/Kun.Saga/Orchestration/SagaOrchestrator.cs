using Kun.Core.Time;
using Kun.Saga.Abstractions;
using Kun.Saga.Persistence;

namespace Kun.Saga.Orchestration;

/// <summary>
/// Default implementation of <see cref="ISagaOrchestrator"/>.
/// Persists the initial <see cref="SagaInstance"/> and enqueues the first step entry within
/// the caller's ambient unit-of-work transaction. All generic dispatch is delegated to
/// AOT-safe typed delegates captured in the <see cref="SagaRegistry"/> at startup.
/// </summary>
public sealed class SagaOrchestrator : ISagaOrchestrator
{
    private readonly ISagaInstanceStore _store;
    private readonly SagaRegistry _registry;
    private readonly ISystemClock _clock;

    /// <summary>Creates a new <see cref="SagaOrchestrator"/>.</summary>
    /// <param name="store">The saga instance persistence store.</param>
    /// <param name="registry">The AOT-safe saga registry.</param>
    /// <param name="clock">The system clock.</param>
    public SagaOrchestrator(ISagaInstanceStore store, SagaRegistry registry, ISystemClock clock)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(clock);
        _store = store;
        _registry = registry;
        _clock = clock;
    }

    /// <inheritdoc/>
    public Task<Guid> StartAsync<TSaga, TContext>(TContext context, CancellationToken cancellationToken = default)
        where TSaga : ISaga<TContext>
        where TContext : ISagaContext
    {
        ArgumentNullException.ThrowIfNull(context);

        var sagaName = typeof(TSaga).Name;
        var registration = _registry.Find(sagaName)
            ?? throw new InvalidOperationException(
                $"Saga '{sagaName}' is not registered. Call AddSaga<{sagaName}, ...>() during startup.");

        // Delegate to the AOT-safe typed delegate captured at registration time.
        return registration.StartAsync(context, _store, _clock, cancellationToken);
    }
}
