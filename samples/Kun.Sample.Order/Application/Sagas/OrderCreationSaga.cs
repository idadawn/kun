using Kun.Saga.Abstractions;
using Kun.Sample.Order.Application.Sagas.Steps;

namespace Kun.Sample.Order.Application.Sagas;

/// <summary>
/// Orchestrates the 4-step order creation flow:
/// ReserveInventory → ProcessPayment → ShipOrder → NotifyCustomer.
/// On failure the orchestrator compensates in reverse order automatically.
/// </summary>
public sealed class OrderCreationSaga : ISaga<OrderCreationSagaContext>
{
    /// <inheritdoc/>
    public string Name => "OrderCreationSaga";

    /// <inheritdoc/>
    public TimeSpan DefaultStepTimeout => TimeSpan.FromSeconds(30);

    /// <inheritdoc/>
    public IReadOnlyList<ISagaStep<OrderCreationSagaContext>> Steps { get; }

    /// <summary>Creates a new <see cref="OrderCreationSaga"/> with DI-injected steps.</summary>
    public OrderCreationSaga(
        ReserveInventoryStep step1,
        ProcessPaymentStep step2,
        ShipOrderStep step3,
        NotifyCustomerStep step4)
    {
        Steps = new ISagaStep<OrderCreationSagaContext>[]
        {
            step1,
            step2,
            step3,
            step4,
        };
    }
}
