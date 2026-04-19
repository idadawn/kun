using Kun.AspNetCore.DependencyInjection;
using Kun.Core.MultiTenancy;
using Kun.Core.Time;
using Kun.EntityFrameworkCore;
using Kun.EntityFrameworkCore.Behaviors;
using Kun.EntityFrameworkCore.DependencyInjection;
using Kun.EventBus;
using Kun.EventBus.Background;
using Kun.EventBus.DependencyInjection;
using Kun.Mediator.DependencyInjection;
using Kun.Saga.DependencyInjection;
using Kun.Sample.Order.Api.Endpoints;
using Kun.Sample.Order.Application.Commands;
using Kun.Sample.Order.Application.Queries;
using Kun.Sample.Order.Application.Sagas;
using Kun.Sample.Order.Application.Sagas.Steps;
using Kun.Sample.Order.Infrastructure;
using Kun.Sample.Order.Infrastructure.Outbox;
using Kun.Sample.Shared;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ── OpenTelemetry (traces + metrics via OTLP) ─────────────────────────────────
builder.Services.AddKunObservability("order");

// ── Logging ──────────────────────────────────────────────────────────────────
builder.Logging.AddConsole();

// ── Core infrastructure ───────────────────────────────────────────────────────
builder.Services.AddSingleton<ISystemClock>(SystemClock.Instance);
// Single-tenant sample — no JWT required.
builder.Services.AddSingleton<ITenantContext>(NullTenantContext.Instance);

// ── EF Core (write side) ──────────────────────────────────────────────────────
var connectionString = builder.Configuration.GetConnectionString("OrderDb")
    ?? "Host=localhost;Database=order_sample;Username=postgres;Password=postgres";

builder.Services.AddDbContext<OrderDbContext>(opt =>
    opt.UseNpgsql(connectionString));

// Register OrderDbContext as KunDbContext so framework infrastructure can resolve it.
builder.Services.AddScoped<KunDbContext>(sp => sp.GetRequiredService<OrderDbContext>());

builder.Services.AddKunEntityFrameworkCore();

// ── Event Bus + Outbox Publisher ──────────────────────────────────────────────
builder.Services.AddKunEventBus(eb =>
{
    eb.Register<Kun.Sample.Order.Contracts.OrderCompletedIntegrationEvent>("order.completed.v1");
    eb.UseInMemoryIdempotency();
});

// Wire the EF-backed outbox store so OutboxPublisherBackgroundService can read messages.
builder.Services.AddScoped<IOutboxMessageStore, EfOutboxMessageStore>();
// No-op IIntegrationEventPublisher — no RabbitMQ configured in this sample.
// Swap with the RabbitMQ publisher from Kun.EventBus.RabbitMQ in production.
builder.Services.AddSingleton<IIntegrationEventPublisher, NoOpIntegrationEventPublisher>();
builder.Services.AddKunOutboxPublisher();

// ── Catalog gRPC client ───────────────────────────────────────────────────────
// Must be registered BEFORE AddKunSaga so that BuildRegistry()'s temporary
// ServiceProvider can resolve ReserveInventoryStep (which depends on this client).
builder.Services
    .AddGrpcClient<Kun.Sample.Catalog.Grpc.CatalogService.CatalogServiceClient>(o =>
    {
        // Aspire service discovery: services__catalog__grpc__0 / services__catalog__http__0 are
        // injected by the AppHost when running under .NET Aspire.
        // For standalone runs, fall back to CatalogGrpc:Address from appsettings.json.
        var catalogUrl = builder.Configuration["services:catalog:grpc:0"]
            ?? builder.Configuration["services:catalog:http:0"]
            ?? builder.Configuration["CatalogGrpc:Address"]
            ?? "http://localhost:5001";
        o.Address = new Uri(catalogUrl);
    });

// ── Saga orchestration ────────────────────────────────────────────────────────
builder.Services.AddScoped<ReserveInventoryStep>();
builder.Services.AddScoped<ProcessPaymentStep>();
builder.Services.AddScoped<ShipOrderStep>();
builder.Services.AddScoped<NotifyCustomerStep>();

builder.Services.AddKunSaga(saga =>
{
    saga.AddSaga<OrderCreationSaga, OrderCreationSagaContext>(
        OrderJsonContext.Default.OrderCreationSagaContext);
});

// ── Mediator ──────────────────────────────────────────────────────────────────
builder.Services.AddKunMediator(m =>
{
    // Commands
    m.AddRequestHandler<PlaceOrderCommand, Guid, PlaceOrderCommandHandler>();
    m.AddPipelineBehavior<PlaceOrderCommand, Guid, UnitOfWorkPipelineBehavior<PlaceOrderCommand, Guid>>();

    // Queries
    m.AddRequestHandler<GetOrderByIdQuery, OrderDto?, GetOrderByIdQueryHandler>();
    m.AddRequestHandler<GetSagaStatusQuery, SagaStatusDto?, GetSagaStatusQueryHandler>();
});

// ── ASP.NET Core ──────────────────────────────────────────────────────────────
builder.Services.AddKunAspNetCore();
builder.Services.AddEndpointsApiExplorer();

var app = builder.Build();

// ── Database bootstrap (apply EF migrations on startup) ──────────────────────
await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
    await db.Database.MigrateAsync();
}

// ── Middleware ────────────────────────────────────────────────────────────────
app.UseExceptionHandler();

// ── Endpoints ─────────────────────────────────────────────────────────────────
app.MapDemoEndpoints();

app.MapGet("/", static () => Results.Ok(new
{
    service = "Kun.Sample.Order",
    description = "Saga orchestration demo",
    endpoints = DemoRoutes.All,
}));

// ── Route manifest (static readonly avoids CA1861 on repeated array allocation) ─

app.Run();

/// <summary>Test affordance — exposes the top-level Program for WebApplicationFactory.</summary>
public partial class Program { }

// ── Route manifest ────────────────────────────────────────────────────────────
file static class DemoRoutes
{
    internal static readonly string[] All =
    [
        "POST /demo/happy-path",
        "POST /demo/trigger-payment-failure",
        "POST /demo/trigger-timeout",
        "GET  /demo/sagas/{id}",
    ];
}

// ── No-op integration event publisher (sample only — no RabbitMQ) ────────────
file sealed class NoOpIntegrationEventPublisher : IIntegrationEventPublisher
{
    public ValueTask PublishAsync(
        Kun.EventBus.Outbox.OutboxMessage message,
        CancellationToken cancellationToken = default)
    {
        // In production replace with RabbitMQ publisher from Kun.EventBus.RabbitMQ.
        Console.WriteLine($"[Outbox] Would publish event '{message.EventTypeId}' (no-op in sample).");
        return ValueTask.CompletedTask;
    }
}
