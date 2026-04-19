using Kun.Core.Cqrs;

namespace Kun.Sample.Order.Application.Queries;

/// <summary>Returns a summary DTO for the given order.</summary>
public sealed record GetOrderByIdQuery(Guid OrderId) : IQuery<OrderDto?>;

/// <summary>Read-side projection of an order.</summary>
public sealed record OrderDto(
    Guid Id,
    Guid CustomerId,
    decimal TotalAmount,
    string Status,
    string? CancellationReason,
    DateTime CreatedAt);
