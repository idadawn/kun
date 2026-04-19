// NOTE: idempotency is demo-grade — a real system would dedupe by saga_id+step_index + product_id.
using Kun.Saga.Abstractions;
using Kun.Saga.Exceptions;
using Kun.Sample.Catalog.Grpc;
using Kun.Sample.Order.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Kun.Sample.Order.Application.Sagas.Steps;

/// <summary>
/// Step 1: Reserve inventory for the order via a real gRPC call to the Catalog service.
/// </summary>
public sealed partial class ReserveInventoryStep : ISagaStep<OrderCreationSagaContext>
{
    private readonly OrderDbContext _db;
    private readonly CatalogService.CatalogServiceClient _catalogClient;
    private readonly ILogger<ReserveInventoryStep> _logger;

    /// <inheritdoc/>
    public string Name => "reserve-inventory";

    /// <inheritdoc/>
    public TimeSpan? Timeout => TimeSpan.FromSeconds(15);

    /// <summary>Creates a new <see cref="ReserveInventoryStep"/>.</summary>
    public ReserveInventoryStep(
        OrderDbContext db,
        CatalogService.CatalogServiceClient catalogClient,
        ILogger<ReserveInventoryStep> logger)
    {
        _db = db;
        _catalogClient = catalogClient;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async ValueTask ExecuteAsync(OrderCreationSagaContext context, CancellationToken cancellationToken)
    {
        LogReserving(_logger, context.SagaId, context.OrderId);

        foreach (var line in context.Items)
        {
            var reply = await _catalogClient.ReserveStockAsync(
                new ReserveStockRequest
                {
                    ProductId = line.ProductId.ToString(),
                    Quantity = line.Quantity,
                },
                cancellationToken: cancellationToken);

            if (!reply.Success)
            {
                throw new SagaStepFailedException(
                    Name,
                    new InvalidOperationException(
                        $"Catalog rejected reservation for product {line.ProductId}: {reply.FailureReason}"));
            }
        }

        var order = await _db.Orders.FirstOrDefaultAsync(o => o.Id == context.OrderId, cancellationToken)
            ?? throw new InvalidOperationException($"Order {context.OrderId} not found.");

        order.MarkReserved();
        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async ValueTask CompensateAsync(OrderCreationSagaContext context, CancellationToken cancellationToken)
    {
        LogReleasing(_logger, context.SagaId, context.OrderId);

        // Restore stock by reserving negative quantities (idempotent: restoring twice is harmless).
        foreach (var line in context.Items)
        {
            try
            {
                await _catalogClient.ReserveStockAsync(
                    new ReserveStockRequest
                    {
                        ProductId = line.ProductId.ToString(),
                        Quantity = -line.Quantity,
                    },
                    cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                LogCompensationWarning(_logger, context.SagaId, line.ProductId, ex);
            }
        }

        var order = await _db.Orders.FirstOrDefaultAsync(o => o.Id == context.OrderId, cancellationToken);
        if (order is null) return; // Already gone — idempotent.

        if (order.Status is not Domain.OrderStatus.Cancelled)
            order.Cancel("Saga compensation: inventory reservation released.");

        await _db.SaveChangesAsync(cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "[Saga {SagaId}] Reserving inventory for order {OrderId} via Catalog gRPC.")]
    private static partial void LogReserving(ILogger logger, Guid sagaId, Guid orderId);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "[Saga {SagaId}] Releasing inventory reservation for order {OrderId} via Catalog gRPC.")]
    private static partial void LogReleasing(ILogger logger, Guid sagaId, Guid orderId);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "[Saga {SagaId}] Compensation call failed for product {ProductId} — stock may need manual reconciliation.")]
    private static partial void LogCompensationWarning(ILogger logger, Guid sagaId, Guid productId, Exception ex);
}
