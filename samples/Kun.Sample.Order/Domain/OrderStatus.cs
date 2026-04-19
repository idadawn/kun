namespace Kun.Sample.Order.Domain;

/// <summary>Lifecycle states of an <see cref="Order"/>.</summary>
public enum OrderStatus
{
    /// <summary>Order has been created and is awaiting processing.</summary>
    Pending,

    /// <summary>Inventory has been reserved for the order.</summary>
    Reserved,

    /// <summary>Payment has been captured.</summary>
    Paid,

    /// <summary>The order has been shipped.</summary>
    Shipped,

    /// <summary>The order has been delivered and all saga steps completed.</summary>
    Completed,

    /// <summary>The order was cancelled (possibly via saga compensation).</summary>
    Cancelled,

    /// <summary>The order failed in a non-recoverable way.</summary>
    Failed,
}
