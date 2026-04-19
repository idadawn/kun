namespace Kun.Sample.Order.Domain;

/// <summary>Owned value object representing a single line on an <see cref="Order"/>.</summary>
public sealed class OrderLine
{
    /// <summary>Gets the product identifier.</summary>
    public Guid ProductId { get; private set; }

    /// <summary>Gets the quantity ordered.</summary>
    public int Quantity { get; private set; }

    /// <summary>Gets the unit price at the time of ordering.</summary>
    public decimal UnitPrice { get; private set; }

    /// <summary>Creates a new <see cref="OrderLine"/>.</summary>
    public OrderLine(Guid productId, int quantity, decimal unitPrice)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);
        ArgumentOutOfRangeException.ThrowIfNegative(unitPrice);

        ProductId = productId;
        Quantity = quantity;
        UnitPrice = unitPrice;
    }

    // EF Core parameterless constructor.
    private OrderLine() { }
}
