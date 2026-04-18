using Kun.Saga.Abstractions;

namespace Kun.Sample.Order.Application.Sagas;

/// <summary>
/// Shared context carried between steps of the <see cref="OrderCreationSaga"/>.
/// Only IDs and key scalars — no large DTOs.
/// </summary>
public sealed class OrderCreationSagaContext : ISagaContext
{
    /// <inheritdoc/>
    public Guid SagaId { get; init; }

    /// <inheritdoc/>
    public string? TenantId { get; init; }

    /// <summary>Gets the order being created.</summary>
    public Guid OrderId { get; init; }

    /// <summary>Gets the customer placing the order.</summary>
    public Guid CustomerId { get; init; }

    /// <summary>Gets the order line summaries (ProductId, Quantity, UnitPrice).</summary>
    public List<OrderLineDto> Items { get; init; } = [];

    /// <summary>Gets the total monetary amount.</summary>
    public decimal TotalAmount { get; init; }

    /// <summary>
    /// Demo affordance: set to <c>"payment"</c> to trigger ProcessPaymentStep failure,
    /// or <c>"timeout"</c> to make ShipOrderStep sleep past its timeout.
    /// </summary>
    public string? SimulateFailure { get; init; }
}

/// <summary>Compact order-line projection stored in the saga context.</summary>
public sealed class OrderLineDto
{
    /// <summary>Gets or sets the product identifier.</summary>
    public Guid ProductId { get; init; }

    /// <summary>Gets or sets the quantity ordered.</summary>
    public int Quantity { get; init; }

    /// <summary>Gets or sets the unit price.</summary>
    public decimal UnitPrice { get; init; }
}
