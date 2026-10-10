using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kun.Saga.Persistence.Ef;

/// <summary>
/// EF Core entity type configuration for <see cref="SagaStepQueueEntry"/>.
/// Table: <c>saga_step_queue</c>.
/// Indexes: <c>(processed_at, enqueued_at)</c> for executor polling.
/// </summary>
public sealed class SagaStepQueueEntryConfiguration : IEntityTypeConfiguration<SagaStepQueueEntry>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<SagaStepQueueEntry> builder)
    {
        builder.ToTable("saga_step_queue");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .IsRequired();

        builder.Property(e => e.SagaId)
            .IsRequired();

        builder.Property(e => e.StepIndex)
            .IsRequired();

        builder.Property(e => e.Direction)
            .IsRequired();

        builder.Property(e => e.EnqueuedAt)
            .IsRequired();

        builder.Property(e => e.ProcessedAt);
        builder.Property(e => e.NextAttemptAt);

        builder.Property(e => e.AttemptCount)
            .IsRequired();

        // Support executor polling: WHERE processed_at IS NULL ORDER BY enqueued_at
        builder.HasIndex(e => new { e.ProcessedAt, e.EnqueuedAt })
            .HasDatabaseName("ix_saga_step_queue_processed_at_enqueued_at");

        // Support loading all steps for a saga instance
        builder.HasIndex(e => e.SagaId)
            .HasDatabaseName("ix_saga_step_queue_saga_id");
    }
}
