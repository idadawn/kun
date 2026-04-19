namespace Kun.Saga.Abstractions;

/// <summary>
/// A single compensatable step in a saga. Each step must implement both a forward
/// <see cref="ISagaStep{TContext}.ExecuteAsync"/> and a reverse
/// <see cref="ISagaStep{TContext}.CompensateAsync"/> path, both of which must be idempotent.
/// </summary>
/// <typeparam name="TContext">The saga context type that carries state between steps.</typeparam>
public interface ISagaStep<TContext>
    where TContext : ISagaContext
{
    /// <summary>Gets the human-readable name of this step, used for logging and diagnostics.</summary>
    string Name { get; }

    /// <summary>
    /// Gets the per-step timeout override. When <see langword="null"/>, the saga's
    /// <see cref="ISaga{TContext}.DefaultStepTimeout"/> is used.
    /// </summary>
    TimeSpan? Timeout { get; }

    /// <summary>
    /// Executes the forward action of this step.
    /// Must be idempotent — may be called more than once due to at-least-once delivery.
    /// </summary>
    /// <param name="context">The saga context carrying state for this execution.</param>
    /// <param name="cancellationToken">Token to observe for cancellation.</param>
    ValueTask ExecuteAsync(TContext context, CancellationToken cancellationToken);

    /// <summary>
    /// Reverses the effects of a previously executed step.
    /// Must be idempotent — may be called even if <see cref="ExecuteAsync"/> never succeeded.
    /// </summary>
    /// <param name="context">The saga context carrying state for this execution.</param>
    /// <param name="cancellationToken">Token to observe for cancellation.</param>
    ValueTask CompensateAsync(TContext context, CancellationToken cancellationToken);
}
