using Kun.EventBus;
using Kun.Saga.Abstractions;
using Kun.Sample.Order.Contracts;
using Kun.Sample.Order.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Kun.Sample.Order.Application.Sagas.Steps;

/// <summary>
/// Step 4: Notify the customer and mark the order completed.
/// Publishes <see cref="OrderCompletedIntegrationEvent"/> via the Outbox.
/// </summary>
public sealed partial class NotifyCustomerStep : ISagaStep<OrderCreationSagaContext>
{
    private readonly OrderDbContext _db;
    private readonly IIntegrationEventBus _eventBus;
    private readonly ILogger<NotifyCustomerStep> _logger;

    /// <inheritdoc/>
    public string Name => "notify-customer";

    /// <inheritdoc/>
    public TimeSpan? Timeout => TimeSpan.FromSeconds(15);

    /// <summary>Creates a new <see cref="NotifyCustomerStep"/>.</summary>
    public NotifyCustomerStep(
        OrderDbContext db,
        IIntegrationEventBus eventBus,
        ILogger<NotifyCustomerStep> logger)
    {
        _db = db;
        _eventBus = eventBus;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async ValueTask ExecuteAsync(OrderCreationSagaContext context, CancellationToken cancellationToken)
    {
        LogNotifying(_logger, context.SagaId, context.CustomerId, context.OrderId);

        var order = await _db.Orders.FirstOrDefaultAsync(o => o.Id == context.OrderId, cancellationToken)
            ?? throw new InvalidOperationException($"Order {context.OrderId} not found.");

        order.MarkCompleted();

        _eventBus.Publish(new OrderCompletedIntegrationEvent(
            OrderId: context.OrderId,
            CustomerId: context.CustomerId,
            TotalAmount: context.TotalAmount,
            CompletedAt: DateTimeOffset.UtcNow));

        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async ValueTask CompensateAsync(OrderCreationSagaContext context, CancellationToken cancellationToken)
    {
        LogReverting(_logger, context.SagaId, context.OrderId);

        var order = await _db.Orders.FirstOrDefaultAsync(o => o.Id == context.OrderId, cancellationToken);
        if (order is null || order.Status is not Domain.OrderStatus.Completed) return;

        order.Cancel("Saga compensation: notification reverted.");
        await _db.SaveChangesAsync(cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "[Saga {SagaId}] Notifying customer {CustomerId} that order {OrderId} is complete.")]
    private static partial void LogNotifying(ILogger logger, Guid sagaId, Guid customerId, Guid orderId);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "[Saga {SagaId}] Reverting customer notification for order {OrderId} (log only).")]
    private static partial void LogReverting(ILogger logger, Guid sagaId, Guid orderId);
}
