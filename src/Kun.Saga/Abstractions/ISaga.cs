namespace Kun.Saga.Abstractions;

/// <summary>
/// Defines an orchestrated saga — an ordered sequence of compensatable <see cref="ISagaStep{TContext}"/>s.
/// The orchestrator executes steps in forward order and compensates in reverse order on failure.
/// </summary>
/// <typeparam name="TContext">The saga context type shared across all steps.</typeparam>
public interface ISaga<TContext>
    where TContext : ISagaContext
{
    /// <summary>
    /// Gets the stable, unique name for this saga type.
    /// Used as the discriminator key in <c>saga_instances.saga_name</c>.
    /// Keep it independent of CLR type names, e.g. <c>order.create.v1</c>. Do not change it while persisted instances still refer to it.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the default timeout applied to each step that does not override
    /// <see cref="ISagaStep{TContext}.Timeout"/>.
    /// The timeout detector will trigger compensation if a step exceeds this duration.
    /// </summary>
    TimeSpan DefaultStepTimeout { get; }

    /// <summary>Gets the ordered list of steps to execute; compensated in reverse on failure.</summary>
    IReadOnlyList<ISagaStep<TContext>> Steps { get; }
}
