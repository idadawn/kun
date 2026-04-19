namespace Kun.Saga.Abstractions;

/// <summary>
/// Starts new saga instances and persists the initial state atomically within the caller's
/// ambient unit-of-work transaction. The background <c>SagaStepExecutor</c> then drives
/// step execution and compensation asynchronously.
/// </summary>
public interface ISagaOrchestrator
{
    /// <summary>
    /// Begins a new saga instance for <typeparamref name="TSaga"/>, persists its initial state,
    /// and enqueues the first step for background execution — all within the caller's current
    /// <c>IUnitOfWork</c> transaction.
    /// </summary>
    /// <typeparam name="TSaga">The saga type to start.</typeparam>
    /// <typeparam name="TContext">The saga context type.</typeparam>
    /// <param name="context">
    /// The initial context for the saga. <see cref="ISagaContext.SagaId"/> is used as the
    /// instance primary key and must be unique across all running sagas.
    /// </param>
    /// <param name="cancellationToken">Token to observe for cancellation.</param>
    /// <returns>The <see cref="Guid"/> saga instance ID (equals <paramref name="context"/>.<see cref="ISagaContext.SagaId"/>).</returns>
    Task<Guid> StartAsync<TSaga, TContext>(TContext context, CancellationToken cancellationToken = default)
        where TSaga : ISaga<TContext>
        where TContext : ISagaContext;
}
