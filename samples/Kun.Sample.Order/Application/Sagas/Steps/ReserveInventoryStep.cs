using Kun.Saga.Abstractions;
using Kun.Sample.Order.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Kun.Sample.Order.Application.Sagas.Steps;

/// <summary>
/// Step 1: Reserve inventory for the order.
/// In production this would gRPC-call the Catalog service — here we log the intent.
/// </summary>
public sealed partial class ReserveInventoryStep : ISagaStep<OrderCreationSagaContext>
{
    private readonly OrderDbContext _db;
    private readonly ILogger<ReserveInventoryStep> _logger;

    /// <inheritdoc/>
    public string Name => "reserve-inventory";

    /// <inheritdoc/>
    public TimeSpan? Timeout => TimeSpan.FromSeconds(15);

    /// <summary>Creates a new <see cref="ReserveInventoryStep"/>.</summary>
    public ReserveInventoryStep(OrderDbContext db, ILogger<ReserveInventoryStep> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async ValueTask ExecuteAsync(OrderCreationSagaContext context, CancellationToken cancellationToken)
    {
        LogReserving(_logger, context.SagaId, context.OrderId);

        var order = await _db.Orders.FirstOrDefaultAsync(o => o.Id == context.OrderId, cancellationToken)
            ?? throw new InvalidOperationException($"Order {context.OrderId} not found.");

        order.MarkReserved();
        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async ValueTask CompensateAsync(OrderCreationSagaContext context, CancellationToken cancellationToken)
    {
        LogReleasing(_logger, context.SagaId, context.OrderId);

        var order = await _db.Orders.FirstOrDefaultAsync(o => o.Id == context.OrderId, cancellationToken);
        if (order is null) return; // Already gone — idempotent.

        if (order.Status is not Domain.OrderStatus.Cancelled)
            order.Cancel("Saga compensation: inventory reservation released.");

        await _db.SaveChangesAsync(cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "[Saga {SagaId}] Reserving inventory for order {OrderId}. Would gRPC-call Catalog service here.")]
    private static partial void LogReserving(ILogger logger, Guid sagaId, Guid orderId);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "[Saga {SagaId}] Releasing inventory reservation for order {OrderId}. Would gRPC-call Catalog service here.")]
    private static partial void LogReleasing(ILogger logger, Guid sagaId, Guid orderId);
}
