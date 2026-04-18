using Kun.Core.Cqrs;
using Kun.Sample.Order.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Kun.Sample.Order.Application.Queries;

/// <summary>Handles <see cref="GetOrderByIdQuery"/> — reads via EF Core (no Dapper needed for sample).</summary>
public sealed class GetOrderByIdQueryHandler : IRequestHandler<GetOrderByIdQuery, OrderDto?>
{
    private readonly OrderDbContext _db;

    /// <summary>Creates a new <see cref="GetOrderByIdQueryHandler"/>.</summary>
    public GetOrderByIdQueryHandler(OrderDbContext db) => _db = db;

    /// <inheritdoc/>
    public async ValueTask<OrderDto?> Handle(GetOrderByIdQuery query, CancellationToken cancellationToken)
    {
        var order = await _db.Orders
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == query.OrderId, cancellationToken);

        return order is null ? null : new OrderDto(
            Id: order.Id,
            CustomerId: order.CustomerId,
            TotalAmount: order.TotalAmount,
            Status: order.Status.ToString(),
            CancellationReason: order.CancellationReason,
            CreatedAt: order.CreatedAt);
    }
}
