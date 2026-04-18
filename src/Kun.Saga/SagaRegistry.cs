using System.Collections.Frozen;
using Kun.Saga.Persistence;

namespace Kun.Saga;

/// <summary>
/// Immutable, AOT-safe registry mapping saga names to their execution delegates.
/// Built at application startup by <see cref="DependencyInjection.KunSagaBuilder"/> and
/// stored as a singleton. Uses a <see cref="FrozenDictionary{TKey,TValue}"/> for O(1) lookup.
/// </summary>
public sealed class SagaRegistry
{
    private readonly FrozenDictionary<string, SagaRegistration> _registrations;

    /// <summary>
    /// Initializes a new <see cref="SagaRegistry"/> from the given registrations.
    /// </summary>
    /// <param name="registrations">All registered saga entries keyed by saga name.</param>
    public SagaRegistry(IEnumerable<SagaRegistration> registrations)
    {
        ArgumentNullException.ThrowIfNull(registrations);
        _registrations = registrations.ToFrozenDictionary(r => r.SagaName);
    }

    /// <summary>
    /// Looks up the registration for a saga by name.
    /// </summary>
    /// <param name="sagaName">The stable saga name (e.g. <c>OrderCreationSaga</c>).</param>
    /// <returns>The <see cref="SagaRegistration"/>, or <see langword="null"/> if not found.</returns>
    public SagaRegistration? Find(string sagaName)
        => _registrations.TryGetValue(sagaName, out var reg) ? reg : null;

    /// <summary>Gets all registered saga names.</summary>
    public IEnumerable<string> RegisteredNames => _registrations.Keys;
}

/// <summary>
/// Holds AOT-safe, closed-over delegates for a single saga type.
/// Generic types are captured at registration time so no runtime <c>MakeGenericType</c>
/// is needed during execution.
/// </summary>
public sealed class SagaRegistration
{
    /// <summary>Gets the stable saga name (e.g. <c>OrderCreationSaga</c>).</summary>
    public string SagaName { get; }

    /// <summary>
    /// Starts a new saga instance. Captures <c>TSaga</c> and <c>TContext</c> at registration
    /// time, so no reflection is required at call time.
    /// Parameters: (contextJson, store, clock, cancellationToken) → sagaId
    /// The context is passed as the typed object (boxed as <see langword="object"/>).
    /// </summary>
    public Func<object, ISagaInstanceStore, Kun.Core.Time.ISystemClock, CancellationToken, Task<Guid>> StartAsync { get; }

    /// <summary>
    /// Executes or compensates a single saga step using the provided context payload JSON.
    /// Captures the concrete <c>TSaga</c>/<c>TContext</c> types at registration time.
    /// Parameters: (stepIndex, direction, contextPayload, serviceProvider, cancellationToken) → Task
    /// </summary>
    public Func<int, StepDirection, string, IServiceProvider, CancellationToken, Task> ExecuteStepAsync { get; }

    /// <summary>Creates a new <see cref="SagaRegistration"/>.</summary>
    /// <param name="sagaName">The stable saga name.</param>
    /// <param name="startAsync">The AOT-safe start delegate.</param>
    /// <param name="executeStepAsync">The AOT-safe step execution delegate.</param>
    public SagaRegistration(
        string sagaName,
        Func<object, ISagaInstanceStore, Kun.Core.Time.ISystemClock, CancellationToken, Task<Guid>> startAsync,
        Func<int, StepDirection, string, IServiceProvider, CancellationToken, Task> executeStepAsync)
    {
        ArgumentNullException.ThrowIfNull(sagaName);
        ArgumentNullException.ThrowIfNull(startAsync);
        ArgumentNullException.ThrowIfNull(executeStepAsync);
        SagaName = sagaName;
        StartAsync = startAsync;
        ExecuteStepAsync = executeStepAsync;
    }
}
