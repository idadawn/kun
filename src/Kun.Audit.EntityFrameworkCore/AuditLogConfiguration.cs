using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kun.Audit.EntityFrameworkCore;

/// <summary>
/// EF Core mapping for <see cref="AuditLog"/>: table name, column shapes,
/// query indexes, and the append-only convention (no concurrency token —
/// rows never update; no soft-delete — rows never delete).
/// </summary>
public sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("audit_logs");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.TenantId).HasMaxLength(64);
        builder.Property(x => x.ActorId).HasMaxLength(128);
        builder.Property(x => x.ActorType).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Action).HasMaxLength(128).IsRequired();
        builder.Property(x => x.ResourceType).HasMaxLength(128).IsRequired();
        builder.Property(x => x.ResourceId).HasMaxLength(256);
        builder.Property(x => x.TraceId).HasMaxLength(64);
        builder.Property(x => x.Timestamp).IsRequired();
        builder.Property(x => x.PayloadJson);
        builder.Property(x => x.IpAddress).HasMaxLength(64);

        builder.HasIndex(x => new { x.TenantId, x.Timestamp })
            .HasDatabaseName("IX_audit_logs_tenant_timestamp")
            .IsDescending(false, true);

        builder.HasIndex(x => new { x.TenantId, x.ActorId, x.Timestamp })
            .HasDatabaseName("IX_audit_logs_tenant_actor_timestamp")
            .IsDescending(false, false, true);

        builder.HasIndex(x => new { x.TenantId, x.Action, x.Timestamp })
            .HasDatabaseName("IX_audit_logs_tenant_action_timestamp")
            .IsDescending(false, false, true);

        builder.HasIndex(x => new { x.TenantId, x.ResourceType, x.ResourceId })
            .HasDatabaseName("IX_audit_logs_tenant_resource");
    }
}
