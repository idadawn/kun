using Kun.EventBus.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kun.EntityFrameworkCore;

/// <summary>EF Core mapping for <see cref="OutboxMessage"/>.</summary>
public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.EventTypeId).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Payload).IsRequired();
        builder.Property(x => x.TenantId).HasMaxLength(64);
        builder.Property(x => x.TraceParent).HasMaxLength(64);
        builder.Property(x => x.TraceState).HasMaxLength(512);
        builder.Property(x => x.OccurredAt).IsRequired();
        builder.Property(x => x.Status).HasConversion<int>().IsRequired();
        builder.Property(x => x.PublishedAt);
        builder.Property(x => x.AttemptCount);
        builder.Property(x => x.NextAttemptAt);
        builder.Property(x => x.LastError).HasMaxLength(2000);
        builder.HasIndex(x => new { x.Status, x.OccurredAt })
            .HasDatabaseName("IX_outbox_messages_status_occurredAt");
    }
}
