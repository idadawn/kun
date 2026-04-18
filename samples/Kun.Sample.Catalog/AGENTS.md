# Kun.Sample.Catalog — Agent Guide

## Purpose

This sample demonstrates the Kun Framework end-to-end in a realistic but bounded scope:
**DDD aggregate → CQRS command/query → Outbox integration event → downstream consumer**.

It is intentionally kept in a single project with logical folders so the entire flow is
readable in one sitting (~30 minutes).

## What is demonstrated

| Concept | Where |
|---|---|
| DDD Aggregate Root | `Domain/Product.cs` — factory method, domain events, soft-delete |
| Value Object | `Domain/Category.cs` — sealed record, structural equality |
| Domain Events | `Domain/Events/*.cs` — raised inside aggregate methods |
| Command + Validator | `Application/Commands/CreateProductCommand*` |
| Command Handler (EF write) | `Application/Commands/CreateProductCommandHandler.cs` |
| Query Handler (Dapper read) | `Application/Queries/GetProductByIdQueryHandler.cs` |
| Domain Event → Integration Event | `Application/DomainEventHandlers/ProductCreatedHandler.cs` |
| Outbox pattern | `Infrastructure/CatalogDbContext.cs` + `EfUnitOfWork` from framework |
| Integration events | `Contracts/ProductPublishedIntegrationEvent.cs`, `InventoryChangedIntegrationEvent.cs` |
| EF Core configuration | `Infrastructure/Configurations/ProductConfiguration.cs` |
| Dapper read connection | `Infrastructure/NpgsqlReadDbConnectionFactory.cs` |
| Domain event dispatch bridge | `Infrastructure/MediatorDomainEventDispatcher.cs` |
| Composition root | `Program.cs` |

## Architecture wiring

```
HTTP POST /products
  → MapCommand (Kun.AspNetCore)
  → ISender.Send(CreateProductCommand)
  → ValidationPipelineBehavior (Kun.Validation)
  → UnitOfWorkPipelineBehavior (Kun.EntityFrameworkCore)
  → CreateProductCommandHandler
      → Product.Create()  [raises ProductCreatedDomainEvent]
      → db.Products.Add(product)
      → IUnitOfWork.SaveChangesAsync()
          → drains IntegrationEventBuffer → OutboxMessages (one EF transaction)
          → dispatches ProductCreatedDomainEvent via MediatorDomainEventDispatcher
              → ProductCreatedHandler.Handle()
                  → IIntegrationEventBus.Publish(ProductPublishedIntegrationEvent)
                     (staged in IntegrationEventBuffer for next commit)
```

## Stable event IDs

| Event | Stable ID |
|---|---|
| `ProductPublishedIntegrationEvent` | `product.published.v1` |
| `InventoryChangedIntegrationEvent` | `inventory.changed.v1` |

## Endpoints

| Method | Path | Description |
|---|---|---|
| POST | `/products` | Create a product (body: `CreateProductCommand` JSON) |
| GET | `/products/{id}` | Fetch a product by ID (Dapper read side) |
| GET | `/health` | Simple liveness check |

## Not included (deferred)

- gRPC endpoints
- EF Core Migrations (uses `EnsureCreated` at startup — suitable for development only)
- Integration tests (requires Docker / Testcontainers)
- Redis idempotency store (uses in-memory for this sample)
- .NET Aspire orchestration (lives in `Kun.Sample.AppHost`)

## Running locally

```bash
# Requires PostgreSQL on localhost:5432 (or override via ConnectionStrings:CatalogDb)
dotnet run --project samples/Kun.Sample.Catalog
```

Set the connection string:
```bash
dotnet run --project samples/Kun.Sample.Catalog \
  --ConnectionStrings:CatalogDb "Host=localhost;Database=catalog;Username=postgres;Password=postgres"
```
