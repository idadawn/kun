using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderAggregate = Kun.Sample.Order.Domain.Order;

namespace Kun.Sample.Order.Infrastructure.Configurations;

/// <summary>EF Core mapping for the <see cref="OrderAggregate"/> aggregate.</summary>
internal sealed class OrderConfiguration : IEntityTypeConfiguration<OrderAggregate>
{
    public void Configure(EntityTypeBuilder<OrderAggregate> builder)
    {
        builder.ToTable("orders");

        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(o => o.CustomerId).HasColumnName("customer_id").IsRequired();
        builder.Property(o => o.TotalAmount).HasColumnName("total_amount").HasPrecision(18, 4);
        builder.Property(o => o.Status).HasColumnName("status").HasConversion<string>().IsRequired();
        builder.Property(o => o.CancellationReason).HasColumnName("cancellation_reason");
        builder.Property(o => o.CreatedAt).HasColumnName("created_at");
        builder.Property(o => o.UpdatedAt).HasColumnName("updated_at");
        builder.Property(o => o.IsDeleted).HasColumnName("is_deleted");
        builder.Property(o => o.DeletedAt).HasColumnName("deleted_at");

        builder.OwnsMany(o => o.Items, lines =>
        {
            lines.ToTable("order_lines");
            lines.WithOwner().HasForeignKey("order_id");
            lines.Property<int>("Id").HasColumnName("id").ValueGeneratedOnAdd();
            lines.HasKey("Id");
            lines.Property(l => l.ProductId).HasColumnName("product_id").IsRequired();
            lines.Property(l => l.Quantity).HasColumnName("quantity");
            lines.Property(l => l.UnitPrice).HasColumnName("unit_price").HasPrecision(18, 4);
        });
    }
}
