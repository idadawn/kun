using Microsoft.EntityFrameworkCore;

namespace Kun.Saga.Persistence.Ef;

/// <summary>
/// Extension methods for <see cref="ModelBuilder"/> to apply Kun Saga entity configurations.
/// Call <see cref="ApplyKunSagaConfiguration"/> from your <c>DbContext.OnModelCreating</c>
/// override to register <c>saga_instances</c> and <c>saga_step_queue</c> tables.
/// </summary>
public static class KunSagaDbContextExtensions
{
    /// <summary>
    /// Applies the EF Core entity type configurations for <see cref="SagaInstance"/> and
    /// <see cref="SagaStepQueueEntry"/> to the given <paramref name="modelBuilder"/>.
    /// </summary>
    /// <param name="modelBuilder">The model builder to configure.</param>
    /// <returns>The same <paramref name="modelBuilder"/> for fluent chaining.</returns>
    public static ModelBuilder ApplyKunSagaConfiguration(this ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfiguration(new SagaInstanceConfiguration());
        modelBuilder.ApplyConfiguration(new SagaStepQueueEntryConfiguration());

        return modelBuilder;
    }
}
