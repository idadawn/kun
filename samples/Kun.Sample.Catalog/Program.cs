using Kun.AspNetCore.DependencyInjection;
using Kun.AspNetCore.Endpoints;
using Kun.Audit.DependencyInjection;
using Kun.Core.Domain;
using Kun.Core.MultiTenancy;
using Kun.Dapper;
using Kun.EntityFrameworkCore;
using Kun.EntityFrameworkCore.Behaviors;
using Kun.EntityFrameworkCore.DependencyInjection;
using Kun.EventBus.DependencyInjection;
using Kun.Grpc.DependencyInjection;
using Kun.Mediator.DependencyInjection;
using Kun.Sample.Catalog;
using Kun.Sample.Catalog.Application.Commands;
using Kun.Sample.Catalog.Application.DomainEventHandlers;
using Kun.Sample.Catalog.Application.Queries;
using Kun.Sample.Catalog.Contracts;
using Kun.Sample.Catalog.Grpc;
using Kun.Sample.Catalog.Infrastructure;
using Kun.Sample.Shared;
using Kun.Sandbox.Extensions;
using Kun.Validation.DependencyInjection;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ── OpenTelemetry (traces + metrics via OTLP) ─────────────────────────────────
builder.Services.AddKunObservability("catalog");

// ── Connection strings ────────────────────────────────────────────────────────
var writeCs = builder.Configuration.GetConnectionString("CatalogDb")
    ?? "Host=localhost;Port=5432;Database=catalog;Username=postgres;Password=postgres";

var readCs = builder.Configuration.GetConnectionString("CatalogDbRead") ?? writeCs;

// ── ASP.NET Core / Kun infrastructure ────────────────────────────────────────
builder.Services.AddKunAspNetCore();

// ── Multi-tenancy (null/single-tenant for this sample) ───────────────────────
builder.Services.AddSingleton<ITenantContext>(NullTenantContext.Instance);

// ── Domain event dispatcher (must be registered before AddKunEntityFrameworkCore
//    so TryAddSingleton does not overwrite it with the NoOp default) ──────────
builder.Services.AddSingleton<IDomainEventDispatcher, MediatorDomainEventDispatcher>();

// ── EF Core (write side) ─────────────────────────────────────────────────────
// Register CatalogDbContext so it can be injected directly by command handlers.
builder.Services.AddDbContext<CatalogDbContext>(opt =>
    opt.UseNpgsql(writeCs));

// EfUnitOfWork resolves KunDbContext, so forward the scoped registration.
builder.Services.AddScoped<KunDbContext>(sp => sp.GetRequiredService<CatalogDbContext>());

builder.Services.AddKunEntityFrameworkCore();

// ── Dapper (read side) ────────────────────────────────────────────────────────
// AddKunDapper uses an Action<ReadDbOptions> callback, but ConnectionString is init-only.
// Register ReadDbOptions directly via object initializer instead.
builder.Services.AddSingleton(new ReadDbOptions { ConnectionString = readCs });
builder.Services.AddSingleton<IReadDbConnectionFactory, NpgsqlReadDbConnectionFactory>();

// ── Event bus + Outbox ────────────────────────────────────────────────────────
builder.Services.AddKunEventBus(eb =>
{
    eb.Register<ProductPublishedIntegrationEvent>("product.published.v1");
    eb.Register<InventoryChangedIntegrationEvent>("inventory.changed.v1");
    eb.UseInMemoryIdempotency();
});

// ── Mediator ──────────────────────────────────────────────────────────────────
builder.Services.AddKunMediator(m =>
{
    // Validation behavior runs before the handler; UoW behavior commits after.
    // Behaviors are applied outermost-first: Validation → UoW → Handler.
    m.AddPipelineBehavior<CreateProductCommand, Guid,
        Kun.Validation.Behaviors.ValidationPipelineBehavior<CreateProductCommand, Guid>>();
    m.AddPipelineBehavior<CreateProductCommand, Guid,
        UnitOfWorkPipelineBehavior<CreateProductCommand, Guid>>();

    m.AddRequestHandler<CreateProductCommand, Guid, CreateProductCommandHandler>();

    m.AddPipelineBehavior<AdjustStockCommand, Kun.Core.Results.Result,
        UnitOfWorkPipelineBehavior<AdjustStockCommand, Kun.Core.Results.Result>>();

    m.AddRequestHandler<AdjustStockCommand, Kun.Core.Results.Result, AdjustStockCommandHandler>();

    m.AddRequestHandler<GetProductByIdQuery, ProductDto?, GetProductByIdQueryHandler>();

    // Domain event notification handler
    m.AddNotificationHandler<ProductCreatedNotification, ProductCreatedHandler>();
});

// Validator for CreateProductCommand
builder.Services.AddValidator<CreateProductCommand, CreateProductCommandValidator>();

// ── Audit (Kun.Audit) ─────────────────────────────────────────────────────────
// Wires IAuditWriter with the NullAuditWriter fallback. Call
// .UseEventBus() to route audit entries through the Outbox, or register
// Kun.Audit.EntityFrameworkCore to persist them to a dedicated table.
// This sample sticks with the null backend so the minimal wiring shape
// is visible without a schema migration.
builder.Services.AddKunAudit();

// ── Sandbox (Kun.Sandbox) ─────────────────────────────────────────────────────
// Demonstrates the in-memory backend — zero network, zero isolation —
// suitable for developer-loop smoke tests and unit tests. Production
// wiring would call UseSupervisor(baseUrl) or UseE2BCloud(apiKey, teamId).
builder.Services.AddKunSandbox(opts => opts.UseInMemory());

// ── gRPC ──────────────────────────────────────────────────────────────────────
builder.Services.AddKunGrpc();

// ── Build ─────────────────────────────────────────────────────────────────────
var app = builder.Build();

app.UseExceptionHandler();

// ── Database initialisation (apply EF migrations on startup) ─────────────────
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
    await db.Database.MigrateAsync();
}

// ── Endpoints ─────────────────────────────────────────────────────────────────
// MapCommand/MapQuery require JsonTypeInfo for AOT-safe serialisation.
// This sample is NOT AOT (IsAotCandidate=false) but we still supply a
// JsonSerializerContext to satisfy the method signature.
app.MapCommand<CreateProductCommand, Guid>(
    "/products",
    CatalogJsonContext.Default.CreateProductCommand,
    CatalogJsonContext.Default.Guid);

// MapQuery cannot express JsonTypeInfo<ProductDto?> via source-gen attributes,
// so this endpoint is wired manually using ISender directly.
app.MapGet("/products/{id:guid}", async (Guid id, Kun.Mediator.ISender sender, CancellationToken ct) =>
{
    var dto = await sender.Send(new GetProductByIdQuery(id), ct);
    return dto is null ? Results.NotFound() : Results.Ok(dto);
});

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.MapGrpcService<CatalogGrpcService>();

app.Run();

/// <summary>Test affordance — exposes the top-level Program for WebApplicationFactory.</summary>
public partial class Program { }

