using Kun.Mediator;
using Kun.Sample.Order.Application.Commands;
using Kun.Sample.Order.Application.Queries;
using Kun.Sample.Order.Application.Sagas;

namespace Kun.Sample.Order.Api.Endpoints;

/// <summary>
/// Demo endpoints that exercise the three Saga paths:
/// happy-path, payment-failure compensation, and timeout compensation.
/// </summary>
public static class DemoEndpoints
{
    /// <summary>Maps all demo routes onto the given <paramref name="app"/>.</summary>
    public static IEndpointRouteBuilder MapDemoEndpoints(this IEndpointRouteBuilder app)
    {
        // POST /demo/happy-path — full saga success
        app.MapPost("/demo/happy-path", async (ISender sender) =>
        {
            var orderId = await sender.Send(BuildCommand(simulateFailure: null));
            return Results.Ok(new { orderId, path = "happy-path" });
        })
        .WithName("HappyPath")
        .WithSummary("Place an order that succeeds all 4 saga steps.");

        // POST /demo/trigger-payment-failure — compensation demo
        app.MapPost("/demo/trigger-payment-failure", async (ISender sender) =>
        {
            var orderId = await sender.Send(BuildCommand(simulateFailure: "payment"));
            return Results.Ok(new { orderId, path = "payment-failure", note = "Saga will compensate ReserveInventory." });
        })
        .WithName("TriggerPaymentFailure")
        .WithSummary("Place an order where ProcessPaymentStep throws, triggering compensation.");

        // POST /demo/trigger-timeout — timeout compensation demo
        app.MapPost("/demo/trigger-timeout", async (ISender sender) =>
        {
            var orderId = await sender.Send(BuildCommand(simulateFailure: "timeout"));
            return Results.Ok(new { orderId, path = "timeout", note = "ShipOrderStep sleeps 60s — SagaTimeoutDetector compensates." });
        })
        .WithName("TriggerTimeout")
        .WithSummary("Place an order where ShipOrderStep exceeds its timeout, triggering compensation.");

        // GET /demo/sagas/{id} — query saga state
        app.MapGet("/demo/sagas/{id:guid}", async (Guid id, ISender sender) =>
        {
            var result = await sender.Send(new GetSagaStatusQuery(id));
            return result is null ? Results.NotFound() : Results.Ok(result);
        })
        .WithName("GetSagaStatus")
        .WithSummary("Get the current state of a saga instance by its ID.");

        return app;
    }

    private static PlaceOrderCommand BuildCommand(string? simulateFailure) =>
        new(
            CustomerId: Guid.NewGuid(),
            Items: new List<OrderLineDto>
            {
                new() { ProductId = Guid.NewGuid(), Quantity = 2, UnitPrice = 49.99m },
                new() { ProductId = Guid.NewGuid(), Quantity = 1, UnitPrice = 19.99m },
            },
            SimulateFailure: simulateFailure);
}
