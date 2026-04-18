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
    /// Convention: <c>PascalCase</c> matching the class name, e.g. <c>OrderCreationSaga</c>.
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
