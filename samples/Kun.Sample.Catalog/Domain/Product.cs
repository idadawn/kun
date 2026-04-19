using Kun.Core.Domain;
using Kun.Sample.Catalog.Domain.Events;

namespace Kun.Sample.Catalog.Domain;

/// <summary>
/// Aggregate root representing a catalog product.
/// All state mutations go through the public methods so domain events are raised correctly.
/// </summary>
public sealed class Product : AggregateRoot<Guid>
{
    /// <summary>Display name of the product.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>Full description of the product.</summary>
    public string Description { get; private set; } = string.Empty;

    /// <summary>Current list price (always ≥ 0).</summary>
    public decimal Price { get; private set; }

    /// <summary>FK to the product's category.</summary>
    public Guid CategoryId { get; private set; }

    /// <summary>Current stock quantity (can be negative to represent backorders).</summary>
    public int Stock { get; private set; }

    // EF Core parameterless constructor
    private Product() { }

    /// <summary>
    /// Factory method — creates a new product and raises <see cref="ProductCreatedDomainEvent"/>.
    /// </summary>
    public static Product Create(
        Guid id,
        string name,
        string description,
        decimal price,
        Guid categoryId,
        int initialStock = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfNegative(price);

        var product = new Product
        {
            Id = id,
            Name = name,
            Description = description,
            Price = price,
            CategoryId = categoryId,
            Stock = initialStock,
            CreatedAt = DateTime.UtcNow,
        };

        product.AddDomainEvent(new ProductCreatedDomainEvent(id, name, price, categoryId));
        return product;
    }

    /// <summary>
    /// Changes the list price and raises <see cref="PriceChangedDomainEvent"/>.
    /// </summary>
    public void ChangePrice(decimal newPrice)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(newPrice);

        if (newPrice == Price)
            return;

        var old = Price;
        Price = newPrice;
        UpdatedAt = DateTime.UtcNow;
        AddDomainEvent(new PriceChangedDomainEvent(Id, old, newPrice));
    }

    /// <summary>
    /// Adjusts stock by <paramref name="delta"/> (positive = restock, negative = sell).
    /// Raises <see cref="StockChangedDomainEvent"/>.
    /// </summary>
    public void AdjustStock(int delta)
    {
        Stock += delta;
        UpdatedAt = DateTime.UtcNow;
        AddDomainEvent(new StockChangedDomainEvent(Id, delta));
    }
}
