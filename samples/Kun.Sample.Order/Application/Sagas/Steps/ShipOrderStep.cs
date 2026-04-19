using Kun.Saga.Abstractions;
using Kun.Sample.Order.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Kun.Sample.Order.Application.Sagas.Steps;

/// <summary>
/// Step 3: Ship the order.
/// Set <see cref="OrderCreationSagaContext.SimulateFailure"/> to <c>"timeout"</c> to make
/// this step sleep beyond its timeout (triggering the SagaTimeoutDetector compensation path).
/// </summary>
public sealed partial class ShipOrderStep : ISagaStep<OrderCreationSagaContext>
{
    private readonly OrderDbContext _db;
    private readonly ILogger<ShipOrderStep> _logger;

    /// <inheritdoc/>
    public string Name => "ship-order";

    /// <inheritdoc/>
    public TimeSpan? Timeout => TimeSpan.FromSeconds(10);

    /// <summary>Creates a new <see cref="ShipOrderStep"/>.</summary>
    public ShipOrderStep(OrderDbContext db, ILogger<ShipOrderStep> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async ValueTask ExecuteAsync(OrderCreationSagaContext context, CancellationToken cancellationToken)
    {
        // Demo affordance: sleep past timeout so SagaTimeoutDetector triggers compensation.
        if (string.Equals(context.SimulateFailure, "timeout", StringComparison.OrdinalIgnoreCase))
        {
            LogTimeoutSimulated(_logger, context.SagaId, context.OrderId);
            // Sleep past the 10s step timeout so SagaTimeoutDetector triggers compensation.
            // 20s is enough to exceed the timeout while keeping the end-to-end test well under its 60s budget.
            await Task.Delay(TimeSpan.FromSeconds(20), cancellationToken);
        }

        LogShipping(_logger, context.SagaId, context.OrderId);

        var order = await _db.Orders.FirstOrDefaultAsync(o => o.Id == context.OrderId, cancellationToken)
            ?? throw new InvalidOperationException($"Order {context.OrderId} not found.");

        order.MarkShipped();
        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async ValueTask CompensateAsync(OrderCreationSagaContext context, CancellationToken cancellationToken)
    {
        LogRecalling(_logger, context.SagaId, context.OrderId);

        var order = await _db.Orders.FirstOrDefaultAsync(o => o.Id == context.OrderId, cancellationToken);
        if (order is null || order.Status is not Domain.OrderStatus.Shipped) return;

        order.Cancel("Saga compensation: shipment recalled.");
        await _db.SaveChangesAsync(cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "[Saga {SagaId}] Simulating timeout for order {OrderId} — sleeping 60s.")]
    private static partial void LogTimeoutSimulated(ILogger logger, Guid sagaId, Guid orderId);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "[Saga {SagaId}] Shipping order {OrderId}.")]
    private static partial void LogShipping(ILogger logger, Guid sagaId, Guid orderId);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "[Saga {SagaId}] Recalling shipment for order {OrderId} (log only — carrier not yet called).")]
    private static partial void LogRecalling(ILogger logger, Guid sagaId, Guid orderId);
}
