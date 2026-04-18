<!-- Parent: ../AGENTS.md -->
<!-- Generated: 2026-04-18 | Updated: 2026-04-18 -->

# Abstractions

## Purpose

Interface-only infrastructure contracts. Business code depends on these interfaces, not on EF Core / Dapper / Redis directly. Concrete implementations live in `Kun.EntityFrameworkCore` (repository, UoW, `EfProcessedMessageStore` if any), `Kun.Dapper` (read-side connection factory), and `Kun.EventBus.RabbitMQ` (`RedisProcessedMessageStore`).

## Key Files

| File | Description |
|------|-------------|
| `IRepository.cs` | `IRepository<T>` (`GetByIdAsync`, `ListAsync(spec)`, `CountAsync(spec)`, `AddAsync`, `Update`, `Remove`) + `IUnitOfWork.SaveChangesAsync` |
| `ISpecification.cs` | `ISpecification<T>` with `Criteria`, `OrderBy`/`OrderByDescending`, `Skip`/`Take`, `IncludeStrings` |
| `Specification.cs` | `Specification<T>` abstract base with protected `AddCriteria` (AND-composes), `ApplyOrderBy*`, `ApplyPaging`, `AddInclude` |
| `IProcessedMessageStore.cs` | `TryMarkAsProcessingAsync(messageId, consumerName)` → bool, `MarkAsCompletedAsync(...)` — backs the idempotent consumer decorator |

## For AI Agents

### Working In This Directory

- **Interfaces only.** No EF Core / Dapper / Redis types may be referenced here. Only `System.Linq.Expressions` (for `ISpecification<T>` expression properties) is acceptable — and only because expression trees are BCL-native.
- **`IRepository.GetByIdAsync` takes `object id`** so the interface isn't parameterized over `TKey`. Concrete implementations cast to `TKey`. Do not "fix" this by adding a `TKey` generic parameter — it would ripple across every repository user.
- **`IUnitOfWork.SaveChangesAsync` carries an implicit Outbox contract**: concrete implementations must commit aggregate changes and Outbox messages in one transaction. Document this in any new implementation.
- **`Specification<T>` composes criteria with AND** via `Expression.AndAlso`. If you need OR composition, add it as a new protected helper rather than repurposing `AddCriteria`.

### Testing Requirements

- Specification composition logic is testable without EF Core — tests can sit in `tests/Kun.Core.Tests` if added.
- Concrete implementations are exercised in `tests/Kun.IntegrationTests`.

### Common Patterns

- Specifications are constructed in application code:
  ```csharp
  public class ActiveProductsSpec : Specification<Product> {
      public ActiveProductsSpec(int page, int size) {
          AddCriteria(p => p.StockQuantity > 0);
          ApplyOrderByDescending(p => p.CreatedAt);
          ApplyPaging(page * size, size);
      }
  }
  ```

## Dependencies

### Internal

- `Kun.Core` — `IAggregateRoot`.

### External

None (`System.Linq.Expressions` is BCL).

<!-- MANUAL: -->
