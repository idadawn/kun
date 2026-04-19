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
    /// Parameters: (contextJson, store, clock, serviceProvider, cancellationToken) → sagaId
    /// The context is passed as the typed object (boxed as <see langword="object"/>).
    /// The <see cref="IServiceProvider"/> is used to resolve the saga's step-0 timeout via
    /// <see cref="GetStepTimeout"/> so that <c>SagaInstance.TimeoutAt</c> reflects the actual
    /// per-step timeout rather than an arbitrary default.
    /// </summary>
    public Func<object, ISagaInstanceStore, Kun.Core.Time.ISystemClock, IServiceProvider, CancellationToken, Task<Guid>> StartAsync { get; }

    /// <summary>
    /// Executes or compensates a single saga step using the provided context payload JSON.
    /// Captures the concrete <c>TSaga</c>/<c>TContext</c> types at registration time.
    /// Parameters: (stepIndex, direction, contextPayload, serviceProvider, cancellationToken) → Task
    /// </summary>
    public Func<int, StepDirection, string, IServiceProvider, CancellationToken, Task> ExecuteStepAsync { get; }

    /// <summary>
    /// Returns the configured <see cref="Kun.Saga.Abstractions.ISagaStep{TContext}.Timeout"/> for
    /// the given step index, or <see langword="null"/> when the step has no timeout.
    /// Used by <c>SagaStepExecutor</c> to set per-step <c>TimeoutAt</c> when advancing.
    /// Parameters: (stepIndex, serviceProvider) → TimeSpan?
    /// </summary>
    public Func<int, IServiceProvider, TimeSpan?> GetStepTimeout { get; }

    /// <summary>
    /// Gets the total number of steps in this saga. Used by <c>SagaStepExecutor</c>
    /// to determine when forward execution is complete.
    /// </summary>
    public int StepCount { get; }

    /// <summary>Creates a new <see cref="SagaRegistration"/>.</summary>
    /// <param name="sagaName">The stable saga name.</param>
    /// <param name="startAsync">The AOT-safe start delegate.</param>
    /// <param name="executeStepAsync">The AOT-safe step execution delegate.</param>
    /// <param name="getStepTimeout">
    /// Returns the timeout for a given step index. May return <see langword="null"/> when
    /// no per-step timeout is configured. Defaults to a delegate that always returns <see langword="null"/>.
    /// </param>
    /// <param name="stepCount">
    /// The total number of steps in this saga. Defaults to <c>0</c> when not provided
    /// (e.g. in unit tests that construct registrations directly without a full saga class).
    /// </param>
    public SagaRegistration(
        string sagaName,
        Func<object, ISagaInstanceStore, Kun.Core.Time.ISystemClock, IServiceProvider, CancellationToken, Task<Guid>> startAsync,
        Func<int, StepDirection, string, IServiceProvider, CancellationToken, Task> executeStepAsync,
        Func<int, IServiceProvider, TimeSpan?>? getStepTimeout = null,
        int stepCount = 0)
    {
        ArgumentNullException.ThrowIfNull(sagaName);
        ArgumentNullException.ThrowIfNull(startAsync);
        ArgumentNullException.ThrowIfNull(executeStepAsync);
        SagaName = sagaName;
        StepCount = stepCount;
        StartAsync = startAsync;
        ExecuteStepAsync = executeStepAsync;
        GetStepTimeout = getStepTimeout ?? ((_, _) => null);
    }
}
