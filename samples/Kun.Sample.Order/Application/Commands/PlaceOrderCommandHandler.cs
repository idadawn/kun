using Kun.Core.Cqrs;
using Kun.Saga.Abstractions;
using Kun.Sample.Order.Application.Sagas;
using Kun.Sample.Order.Infrastructure;
using OrderAggregate = Kun.Sample.Order.Domain.Order;
using OrderLine = Kun.Sample.Order.Domain.OrderLine;

namespace Kun.Sample.Order.Application.Commands;

/// <summary>
/// Handles <see cref="PlaceOrderCommand"/>: creates the <see cref="OrderAggregate"/> aggregate,
/// persists it, and starts the <see cref="OrderCreationSaga"/> in the same unit-of-work.
/// </summary>
public sealed partial class PlaceOrderCommandHandler : IRequestHandler<PlaceOrderCommand, Guid>
{
    private readonly OrderDbContext _db;
    private readonly ISagaOrchestrator _sagaOrchestrator;
    private readonly ILogger<PlaceOrderCommandHandler> _logger;

    /// <summary>Creates a new <see cref="PlaceOrderCommandHandler"/>.</summary>
    public PlaceOrderCommandHandler(
        OrderDbContext db,
        ISagaOrchestrator sagaOrchestrator,
        ILogger<PlaceOrderCommandHandler> logger)
    {
        _db = db;
        _sagaOrchestrator = sagaOrchestrator;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async ValueTask<Guid> Handle(PlaceOrderCommand cmd, CancellationToken cancellationToken)
    {
        var lines = cmd.Items.Select(i => new OrderLine(i.ProductId, i.Quantity, i.UnitPrice));
        var order = OrderAggregate.Create(cmd.CustomerId, lines);

        await _db.Orders.AddAsync(order, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);

        var sagaCtx = new OrderCreationSagaContext
        {
            SagaId = Guid.NewGuid(),
            TenantId = null,
            OrderId = order.Id,
            CustomerId = cmd.CustomerId,
            Items = cmd.Items,
            TotalAmount = order.TotalAmount,
            SimulateFailure = cmd.SimulateFailure,
        };

        var sagaId = await _sagaOrchestrator.StartAsync<OrderCreationSaga, OrderCreationSagaContext>(
            sagaCtx, cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        LogOrderPlaced(_logger, order.Id, sagaId);
        return order.Id;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Order {OrderId} placed. Saga {SagaId} started.")]
    private static partial void LogOrderPlaced(ILogger logger, Guid orderId, Guid sagaId);
}
