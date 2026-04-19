using Kun.Saga.Abstractions;
using Kun.Saga.Exceptions;
using Kun.Sample.Order.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Kun.Sample.Order.Application.Sagas.Steps;

/// <summary>
/// Step 2: Process payment for the order.
/// Set <see cref="OrderCreationSagaContext.SimulateFailure"/> to <c>"payment"</c> to
/// trigger the compensation path (demo affordance).
/// </summary>
public sealed partial class ProcessPaymentStep : ISagaStep<OrderCreationSagaContext>
{
    private readonly OrderDbContext _db;
    private readonly ILogger<ProcessPaymentStep> _logger;

    /// <inheritdoc/>
    public string Name => "process-payment";

    /// <inheritdoc/>
    public TimeSpan? Timeout => TimeSpan.FromSeconds(20);

    /// <summary>Creates a new <see cref="ProcessPaymentStep"/>.</summary>
    public ProcessPaymentStep(OrderDbContext db, ILogger<ProcessPaymentStep> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async ValueTask ExecuteAsync(OrderCreationSagaContext context, CancellationToken cancellationToken)
    {
        // Demo affordance: simulate a payment gateway failure.
        if (string.Equals(context.SimulateFailure, "payment", StringComparison.OrdinalIgnoreCase))
        {
            LogPaymentSimulated(_logger, context.SagaId, context.OrderId);
            throw new SagaStepFailedException(Name,
                new InvalidOperationException("Payment gateway rejected the transaction (simulated)."));
        }

        LogProcessing(_logger, context.SagaId, context.TotalAmount, context.OrderId);

        var order = await _db.Orders.FirstOrDefaultAsync(o => o.Id == context.OrderId, cancellationToken)
            ?? throw new InvalidOperationException($"Order {context.OrderId} not found.");

        order.MarkPaid();
        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async ValueTask CompensateAsync(OrderCreationSagaContext context, CancellationToken cancellationToken)
    {
        LogRefunding(_logger, context.SagaId, context.OrderId);

        // Payment may not have completed — only cancel if order reached Paid.
        var order = await _db.Orders.FirstOrDefaultAsync(o => o.Id == context.OrderId, cancellationToken);
        if (order is null || order.Status is not Domain.OrderStatus.Paid) return;

        order.Cancel("Saga compensation: payment refunded.");
        await _db.SaveChangesAsync(cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "[Saga {SagaId}] Simulating payment failure for order {OrderId}.")]
    private static partial void LogPaymentSimulated(ILogger logger, Guid sagaId, Guid orderId);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "[Saga {SagaId}] Processing payment of {Amount:C} for order {OrderId}.")]
    private static partial void LogProcessing(ILogger logger, Guid sagaId, decimal amount, Guid orderId);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "[Saga {SagaId}] Refunding payment for order {OrderId} (idempotent — log only).")]
    private static partial void LogRefunding(ILogger logger, Guid sagaId, Guid orderId);
}
