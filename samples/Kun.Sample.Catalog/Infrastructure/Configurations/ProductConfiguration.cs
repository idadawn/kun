using Kun.Sample.Catalog.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kun.Sample.Catalog.Infrastructure.Configurations;

/// <summary>EF Core mapping for the <see cref="Product"/> aggregate.</summary>
public sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("catalog_products");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Name)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(p => p.Description)
            .HasMaxLength(2000)
            .IsRequired();

        builder.Property(p => p.Price)
            .HasColumnType("numeric(18,4)")
            .IsRequired();

        builder.Property(p => p.CategoryId)
            .IsRequired();

        builder.Property(p => p.Stock)
            .IsRequired();

        builder.Property(p => p.CreatedAt).IsRequired();
        builder.Property(p => p.UpdatedAt);
        builder.Property(p => p.IsDeleted).IsRequired();
        builder.Property(p => p.DeletedAt);

        // Optimistic index: look up by category
        builder.HasIndex(p => p.CategoryId)
            .HasDatabaseName("IX_catalog_products_category_id");

        // Soft-delete filter index
        builder.HasIndex(p => p.IsDeleted)
            .HasDatabaseName("IX_catalog_products_is_deleted");
    }
}
