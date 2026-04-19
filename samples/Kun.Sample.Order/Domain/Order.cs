using Kun.Core.Domain;
using Kun.Sample.Order.Domain.Events;

namespace Kun.Sample.Order.Domain;

/// <summary>
/// Order aggregate root. Tracks the lifecycle of a customer order through the
/// saga orchestration pipeline (Pending → Reserved → Paid → Shipped → Completed).
/// </summary>
public sealed class Order : AggregateRoot<Guid>
{
    private readonly List<OrderLine> _items = new();

    /// <summary>Gets the customer who placed this order.</summary>
    public Guid CustomerId { get; private set; }

    /// <summary>Gets the order lines.</summary>
    public IReadOnlyList<OrderLine> Items => _items.AsReadOnly();

    /// <summary>Gets the total monetary amount of this order.</summary>
    public decimal TotalAmount { get; private set; }

    /// <summary>Gets the current lifecycle status.</summary>
    public OrderStatus Status { get; private set; }

    /// <summary>Gets the cancellation reason; <see langword="null"/> when not cancelled.</summary>
    public string? CancellationReason { get; private set; }

    // EF Core parameterless constructor.
    private Order() { }

    private Order(Guid id, Guid customerId, IEnumerable<OrderLine> items)
    {
        Id = id;
        CustomerId = customerId;
        _items.AddRange(items);
        TotalAmount = _items.Sum(l => l.Quantity * l.UnitPrice);
        Status = OrderStatus.Pending;
        CreatedAt = DateTime.UtcNow;

        AddDomainEvent(new OrderPlacedDomainEvent(Id, CustomerId, TotalAmount));
    }

    /// <summary>Creates a new <see cref="Order"/> in Pending status.</summary>
    public static Order Create(Guid customerId, IEnumerable<OrderLine> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return new Order(Guid.NewGuid(), customerId, items);
    }

    /// <summary>Transitions the order to <see cref="OrderStatus.Reserved"/> after inventory is confirmed.</summary>
    public void MarkReserved()
    {
        EnsureStatus(OrderStatus.Pending);
        Status = OrderStatus.Reserved;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Transitions the order to <see cref="OrderStatus.Paid"/> after payment succeeds.</summary>
    public void MarkPaid()
    {
        EnsureStatus(OrderStatus.Reserved);
        Status = OrderStatus.Paid;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Transitions the order to <see cref="OrderStatus.Shipped"/>.</summary>
    public void MarkShipped()
    {
        EnsureStatus(OrderStatus.Paid);
        Status = OrderStatus.Shipped;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Transitions the order to <see cref="OrderStatus.Completed"/>.</summary>
    public void MarkCompleted()
    {
        EnsureStatus(OrderStatus.Shipped);
        Status = OrderStatus.Completed;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Cancels the order with a human-readable <paramref name="reason"/>.</summary>
    public void Cancel(string reason)
    {
        ArgumentNullException.ThrowIfNull(reason);
        if (Status is OrderStatus.Completed or OrderStatus.Cancelled)
            throw new InvalidOperationException($"Cannot cancel order in status {Status}.");

        Status = OrderStatus.Cancelled;
        CancellationReason = reason;
        UpdatedAt = DateTime.UtcNow;

        AddDomainEvent(new OrderCancelledDomainEvent(Id, reason));
    }

    private void EnsureStatus(OrderStatus expected)
    {
        if (Status != expected)
            throw new InvalidOperationException(
                $"Expected order status {expected} but found {Status}.");
    }
}
