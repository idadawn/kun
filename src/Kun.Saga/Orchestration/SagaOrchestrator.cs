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
    private readonly IServiceProvider _serviceProvider;

    /// <summary>Creates a new <see cref="SagaOrchestrator"/>.</summary>
    /// <param name="store">The saga instance persistence store.</param>
    /// <param name="registry">The AOT-safe saga registry.</param>
    /// <param name="clock">The system clock.</param>
    /// <param name="serviceProvider">
    /// The scoped service provider used to resolve the saga instance so its step-0 timeout
    /// can be read when computing the initial <c>SagaInstance.TimeoutAt</c>.
    /// </param>
    public SagaOrchestrator(ISagaInstanceStore store, SagaRegistry registry, ISystemClock clock, IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(serviceProvider);
        _store = store;
        _registry = registry;
        _clock = clock;
        _serviceProvider = serviceProvider;
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
        // Pass the service provider so the delegate can resolve step-0 timeout.
        return registration.StartAsync(context, _store, _clock, _serviceProvider, cancellationToken);
    }
}
