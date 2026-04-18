using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kun.Saga.Persistence.Ef;

/// <summary>
/// EF Core entity type configuration for <see cref="SagaInstance"/>.
/// Table: <c>saga_instances</c>.
/// Indexes: <c>(state, timeout_at)</c> for timeout scanning; <c>(saga_name)</c> for name filtering.
/// </summary>
public sealed class SagaInstanceConfiguration : IEntityTypeConfiguration<SagaInstance>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<SagaInstance> builder)
    {
        builder.ToTable("saga_instances");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Id)
            .IsRequired();

        builder.Property(s => s.SagaName)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(s => s.TenantId)
            .HasMaxLength(128);

        builder.Property(s => s.ContextPayload)
            .IsRequired();

        builder.Property(s => s.CurrentStepIndex)
            .IsRequired();

        builder.Property(s => s.State)
            .IsRequired();

        builder.Property(s => s.StartedAt)
            .IsRequired();

        builder.Property(s => s.UpdatedAt)
            .IsRequired();

        builder.Property(s => s.TimeoutAt);

        builder.Property(s => s.CompletedAt);

        builder.Property(s => s.FailureReason)
            .HasMaxLength(2048);

        builder.Property(s => s.TraceId)
            .HasMaxLength(256);

        // Support timeout scanning: WHERE state = Running AND timeout_at < now
        builder.HasIndex(s => new { s.State, s.TimeoutAt })
            .HasDatabaseName("ix_saga_instances_state_timeout_at");

        // Support lookup by saga type name
        builder.HasIndex(s => s.SagaName)
            .HasDatabaseName("ix_saga_instances_saga_name");
    }
}
