using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Kun.Core.Time;
using Kun.Saga.Abstractions;
using Kun.Saga.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Kun.Saga.DependencyInjection;

/// <summary>
/// Builder for registering saga types and their steps with the Kun Saga infrastructure.
/// Obtained via <see cref="KunSagaServiceCollectionExtensions.AddKunSaga"/>.
/// </summary>
public sealed class KunSagaBuilder
{
    private readonly IServiceCollection _services;

    // Holds (partial registration without StepCount, step-count resolver) pairs.
    // StepCount is resolved from a temporary IServiceProvider in BuildRegistry().
    private readonly List<(SagaRegistration Partial, Func<IServiceProvider, int> StepCountResolver)> _pending = new();

    internal KunSagaBuilder(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = services;
    }

    /// <summary>
    /// Registers a saga type and its associated context. The saga is resolved from DI as a
    /// singleton; steps are resolved as scoped services per execution.
    /// </summary>
    /// <typeparam name="TSaga">The saga type implementing <see cref="ISaga{TContext}"/>.</typeparam>
    /// <typeparam name="TContext">The saga context type implementing <see cref="ISagaContext"/>.</typeparam>
    /// <param name="typeInfo">
    /// The <see cref="JsonTypeInfo{T}"/> for <typeparamref name="TContext"/> used for AOT-safe
    /// JSON serialization. Obtain from a source-generated <c>JsonSerializerContext</c>.
    /// </param>
    /// <returns>The same builder for fluent chaining.</returns>
    public KunSagaBuilder AddSaga<TSaga, TContext>(JsonTypeInfo<TContext> typeInfo)
        where TSaga : class, ISaga<TContext>
        where TContext : ISagaContext
    {
        ArgumentNullException.ThrowIfNull(typeInfo);

        _services.AddSingleton<TSaga>();

        var sagaName = typeof(TSaga).Name;

        // AOT-safe start delegate: captures TSaga, TContext, and typeInfo by closure.
        // No MakeGenericType at runtime.
        Task<Guid> StartDelegate(
            object contextObj,
            ISagaInstanceStore store,
            ISystemClock clock,
            IServiceProvider sp,
            CancellationToken ct)
        {
            var context = (TContext)contextObj;
            var now = clock.UtcNow;
            var payload = JsonSerializer.Serialize(context, typeInfo);

            // Use step-0's per-step timeout (falling back to the saga's DefaultStepTimeout,
            // then to 5 min if DefaultStepTimeout is somehow unset).
            var saga = sp.GetRequiredService<TSaga>();
            var step0Timeout = (saga.Steps.Count > 0 ? saga.Steps[0].Timeout : null)
                ?? saga.DefaultStepTimeout;
            var timeoutAt = now.Add(step0Timeout);
            var traceId = Activity.Current?.Id;

            var instance = new SagaInstance(
                id: context.SagaId,
                sagaName: sagaName,
                tenantId: context.TenantId,
                contextPayload: payload,
                startedAt: now,
                timeoutAt: timeoutAt,
                traceId: traceId);

            var firstEntry = new SagaStepQueueEntry(
                id: Guid.NewGuid(),
                sagaId: context.SagaId,
                stepIndex: 0,
                direction: StepDirection.Execute,
                enqueuedAt: now);

            return StartCoreAsync(instance, firstEntry, store, ct);
        }

        // AOT-safe step execution delegate: captures TSaga, TContext, and typeInfo by closure.
        async Task ExecuteStepDelegate(
            int stepIndex,
            StepDirection direction,
            string contextPayload,
            IServiceProvider serviceProvider,
            CancellationToken ct)
        {
            var saga = serviceProvider.GetRequiredService<TSaga>();
            var context = JsonSerializer.Deserialize(contextPayload, typeInfo)
                ?? throw new InvalidOperationException(
                    $"Failed to deserialize context for saga '{sagaName}'.");

            if (stepIndex < 0 || stepIndex >= saga.Steps.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(stepIndex),
                    $"Step index {stepIndex} is out of range for saga '{sagaName}' (steps: {saga.Steps.Count}).");
            }

            var step = saga.Steps[stepIndex];

            if (direction == StepDirection.Execute)
            {
                await step.ExecuteAsync(context, ct).ConfigureAwait(false);
            }
            else
            {
                await step.CompensateAsync(context, ct).ConfigureAwait(false);
            }
        }

        // Returns the Timeout for a given step index by resolving the saga from DI.
        TimeSpan? GetStepTimeoutDelegate(int stepIndex, IServiceProvider sp)
        {
            var saga = sp.GetRequiredService<TSaga>();
            if (stepIndex < 0 || stepIndex >= saga.Steps.Count) return null;
            return saga.Steps[stepIndex].Timeout;
        }

        // Partial registration without StepCount — resolved in BuildRegistry().
        var partial = new SagaRegistration(
            sagaName: sagaName,
            startAsync: StartDelegate,
            executeStepAsync: ExecuteStepDelegate,
            getStepTimeout: GetStepTimeoutDelegate,
            stepCount: 0);

        _pending.Add((partial, sp => sp.GetRequiredService<TSaga>().Steps.Count));

        return this;
    }

    internal SagaRegistry BuildRegistry()
    {
        // Build a temporary service provider to resolve each saga singleton once
        // so we can read its Steps.Count and store it in the final SagaRegistration.
        using var tempProvider = _services.BuildServiceProvider();

        var registrations = _pending.Select(p =>
        {
            var stepCount = p.StepCountResolver(tempProvider);
            return new SagaRegistration(
                sagaName: p.Partial.SagaName,
                startAsync: p.Partial.StartAsync,
                executeStepAsync: p.Partial.ExecuteStepAsync,
                getStepTimeout: p.Partial.GetStepTimeout,
                stepCount: stepCount);
        });

        return new SagaRegistry(registrations);
    }

    private static async Task<Guid> StartCoreAsync(
        SagaInstance instance,
        SagaStepQueueEntry firstEntry,
        ISagaInstanceStore store,
        CancellationToken ct)
    {
        await store.AddAsync(instance, ct).ConfigureAwait(false);
        await store.EnqueueStepAsync(firstEntry, ct).ConfigureAwait(false);
        return instance.Id;
    }
}
