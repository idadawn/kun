using Kun.Core.Cqrs;
using Kun.Sample.Order.Application.Sagas;

namespace Kun.Sample.Order.Application.Commands;

/// <summary>Places a new order, persists it, and starts the <c>OrderCreationSaga</c>.</summary>
/// <param name="CustomerId">The customer placing the order.</param>
/// <param name="Items">The line items requested.</param>
/// <param name="SimulateFailure">
/// Optional demo affordance: <c>"payment"</c> triggers payment failure compensation;
/// <c>"timeout"</c> makes ShipOrderStep sleep past its timeout.
/// </param>
public sealed record PlaceOrderCommand(
    Guid CustomerId,
    List<OrderLineDto> Items,
    string? SimulateFailure = null) : ICommand<Guid>;
