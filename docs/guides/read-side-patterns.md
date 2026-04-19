# Read-Side Patterns: Writing Query Handlers

This guide covers the recommended patterns for query handlers in Kun. It assumes you understand
the write/read split; for the reasoning behind that split see
[docs/architecture/why-cqrs.md](../architecture/why-cqrs.md).

---

## The Write/Read Rule

Query handlers must not use `KunDbContext`, `IUnitOfWork`, or any EF Core type. They connect to
the database via `IReadDbConnectionFactory`, run raw SQL with Dapper, and return flat DTOs. The
boundary is enforced by module dependencies: `Kun.Dapper` does not reference
`Kun.EntityFrameworkCore`, so adding an EF Core type to a query handler requires a deliberate
csproj change that reviewers will catch.

The short version:

- Command handler: inject `IRepository<T>` or `KunDbContext` (via EF). Transactional.
- Query handler: inject `IReadDbConnectionFactory`. No change tracking, no aggregates.

---

## Recommended Query Handler Pattern

The standard shape for a Kun query handler:

```csharp
using Dapper;
using Kun.Core.Cqrs;
using Kun.Dapper;

public sealed class GetProductByIdQueryHandler(IReadDbConnectionFactory connectionFactory)
    : IRequestHandler<GetProductByIdQuery, ProductDto?>
{
    public async ValueTask<ProductDto?> Handle(
        GetProductByIdQuery request,
        CancellationToken cancellationToken)
    {
        using var connection = await connectionFactory.OpenAsync(cancellationToken);

        const string sql = """
            SELECT id          AS "Id",
                   name        AS "Name",
                   description AS "Description",
                   price       AS "Price",
                   category_id AS "CategoryId",
                   stock       AS "Stock"
            FROM   catalog_products
            WHERE  id = @Id
              AND  is_deleted = false
            """;

        return await connection.QuerySingleOrDefaultAsync<ProductDto>(
            sql,
            new { request.Id });
    }
}
```

This is the actual handler from
`samples/Kun.Sample.Catalog/Application/Queries/GetProductByIdQueryHandler.cs`.

Key points:

- **`IReadDbConnectionFactory.OpenAsync`** returns an open `IDbConnection`. Wrap it in `using` —
  the factory opens a fresh connection per call and the caller is responsible for disposal.
- **Parameterized SQL** via the anonymous object `new { request.Id }`. Never interpolate values
  directly into the SQL string.
- **Column aliases** match the DTO property names exactly (case-sensitive on PostgreSQL). Use the
  `AS "PropertyName"` pattern consistently.
- **Soft-delete filter** (`is_deleted = false`) applied directly in SQL. There is no EF Core
  global filter on the read side — the SQL is the filter.
- The handler returns `null` when no row matches, which maps to a `404 Not Found` response via
  `Kun.AspNetCore`'s `Error.ToProblemDetails()` mapping.

---

## IReadDbConnectionFactory

`src/Kun.Dapper/IReadDbConnectionFactory.cs`:

```csharp
public interface IReadDbConnectionFactory
{
    Task<IDbConnection> OpenAsync(CancellationToken cancellationToken = default);
}
```

The factory is a thin abstraction over a concrete ADO.NET provider. The sample implementation
using Npgsql (`samples/Kun.Sample.Catalog/Infrastructure/NpgsqlReadDbConnectionFactory.cs`):

```csharp
public sealed class NpgsqlReadDbConnectionFactory(ReadDbOptions options)
    : IReadDbConnectionFactory
{
    public async Task<IDbConnection> OpenAsync(CancellationToken cancellationToken = default)
    {
        var connection = new NpgsqlConnection(options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }
}
```

Register it in `Program.cs`:

```csharp
builder.Services.AddKunDapper(opts =>
    opts = new ReadDbOptions { ConnectionString = connectionString });

builder.Services.AddSingleton<IReadDbConnectionFactory, NpgsqlReadDbConnectionFactory>();
```

`AddKunDapper` is in `src/Kun.Dapper/DependencyInjection/KunDapperServiceCollectionExtensions.cs`.
It registers the `ReadDbOptions` singleton. Your `IReadDbConnectionFactory` implementation
receives it via constructor injection.

Note: `ReadDbOptions.ConnectionString` is `required init` — you must set it when constructing the
options object. The `AddKunDapper` overload shown above takes an `Action<ReadDbOptions>` callback;
set all required properties inside that callback.

---

## PageRequest and PagedResult

Use `PageRequest` and `PagedResult<T>` for paginated queries.

**`PageRequest`** (`src/Kun.Dapper/PageRequest.cs`):

```csharp
public sealed record PageRequest(int Page = 1, int PageSize = 20)
{
    public int Offset => (Page - 1) * PageSize;
}
```

- `Page` is 1-based.
- `Offset` computes the SQL `OFFSET` value automatically.

**`PagedResult<T>`** (`src/Kun.Dapper/PagedResult.cs`):

```csharp
public sealed class PagedResult<T>
{
    public IReadOnlyList<T> Items { get; init; } = [];
    public int TotalCount { get; init; }
    public required PageRequest Request { get; init; }

    public int TotalPages =>
        TotalCount == 0 ? 0 : (int)Math.Ceiling((double)TotalCount / Request.PageSize);
    public bool HasNextPage => Request.Page < TotalPages;
    public bool HasPreviousPage => Request.Page > 1;

    public static PagedResult<T> Empty(PageRequest request) =>
        new() { Request = request, TotalCount = 0 };
}
```

A paginated handler pattern:

```csharp
public sealed class ListProductsQueryHandler(IReadDbConnectionFactory connectionFactory)
    : IRequestHandler<ListProductsQuery, PagedResult<ProductDto>>
{
    public async ValueTask<PagedResult<ProductDto>> Handle(
        ListProductsQuery request,
        CancellationToken cancellationToken)
    {
        using var connection = await connectionFactory.OpenAsync(cancellationToken);

        const string countSql = """
            SELECT COUNT(*) FROM catalog_products WHERE is_deleted = false
            """;

        const string itemsSql = """
            SELECT id          AS "Id",
                   name        AS "Name",
                   price       AS "Price",
                   stock       AS "Stock"
            FROM   catalog_products
            WHERE  is_deleted = false
            ORDER  BY name
            LIMIT  @PageSize OFFSET @Offset
            """;

        var totalCount = await connection.ExecuteScalarAsync<int>(countSql);

        var items = await connection.QueryAsync<ProductDto>(
            itemsSql,
            new { request.Page.PageSize, request.Page.Offset });

        return new PagedResult<ProductDto>
        {
            Items      = items.ToArray(),
            TotalCount = totalCount,
            Request    = request.Page,
        };
    }
}
```

Run the count and items queries in the same open connection. Do not open two connections — use
Dapper's `QueryMultiple` if you prefer a single round-trip:

```csharp
const string sql = """
    SELECT COUNT(*) FROM catalog_products WHERE is_deleted = false;
    SELECT id AS "Id", name AS "Name", price AS "Price", stock AS "Stock"
    FROM   catalog_products
    WHERE  is_deleted = false
    ORDER  BY name
    LIMIT  @PageSize OFFSET @Offset;
    """;

using var multi = await connection.QueryMultipleAsync(
    sql,
    new { request.Page.PageSize, request.Page.Offset });

var totalCount = await multi.ReadFirstAsync<int>();
var items      = (await multi.ReadAsync<ProductDto>()).ToArray();
```

---

## DapperAotExtensions.QueryPageAsync

A `QueryPageAsync` extension that bundles the count + items dual-query into a single call is
planned (see TODO 2.3). Until it lands, use the patterns shown above directly.

---

## DTO Design

DTOs on the read side are intentionally flat. They are shaped for the API consumer, not for the
domain model.

Rules:

- **No aggregate nesting.** A `ProductDto` does not contain an `OrderDto`. If a view needs data
  from two aggregates, join the tables in SQL.
- **No domain types.** DTOs use primitive types (`string`, `decimal`, `Guid`, `int`) or simple
  value types. Never expose `Money`, `Address`, or other value objects from the domain layer in a
  DTO — that would create a dependency on `Kun.Core` in the read result.
- **Name for the consumer, not the table.** Column aliases can differ from table column names.
  Shape the DTO for what the API consumer needs to display or act on.
- **Immutable records.** Use `sealed record` with positional properties for DTOs. Records are
  value-comparable, serializable, and clearly signal "data container, not entity".

The `ProductDto` from the catalog sample:

```csharp
// samples/Kun.Sample.Catalog/Application/Queries/ProductDto.cs
public sealed record ProductDto(
    Guid Id,
    string Name,
    string Description,
    decimal Price,
    Guid CategoryId,
    int Stock);
```

The query from `GetProductByIdQuery`:

```csharp
// samples/Kun.Sample.Catalog/Application/Queries/GetProductByIdQuery.cs
public sealed record GetProductByIdQuery(Guid Id) : IQuery<ProductDto?>;
```

`IQuery<TResponse>` is a marker interface in `Kun.Core.Cqrs` that extends `IRequest<TResponse>`.
Using `IQuery` rather than `IRequest` directly signals intent and allows pipeline behaviors to
discriminate between commands and queries at registration time.

---

## Anti-Patterns

### Do not inject KunDbContext in a query handler

```csharp
// Wrong — do not do this
public sealed class GetProductByIdQueryHandler(CatalogDbContext db)
    : IRequestHandler<GetProductByIdQuery, ProductDto?>
{
    public async ValueTask<ProductDto?> Handle(...)
    {
        return await db.Products
            .Where(p => p.Id == request.Id)
            .Select(p => new ProductDto(...))
            .FirstOrDefaultAsync(cancellationToken);
    }
}
```

Problems with this pattern:

1. `CatalogDbContext` inherits `KunDbContext` which has global query filters, change tracking, and
   concurrency checks active. A query handler does not need any of this and pays the overhead.
2. `Kun.EntityFrameworkCore` is not AOT-candidate (`IsAotCandidate=false`). Injecting it into a
   query handler that runs in an AOT-published service will fail at publish time with trim warnings
   or at runtime with reflection errors.
3. `UnitOfWorkPipelineBehavior` calls `SaveChangesAsync` after every registered handler in the
   write pipeline. If a query handler is registered in the same pipeline stack as a command
   handler that has `UnitOfWorkPipelineBehavior`, the query will trigger a `SaveChangesAsync`
   call. This is harmless but wasteful — and it signals a pipeline configuration mistake.

### Do not load aggregates and project at runtime

```csharp
// Wrong — loads the entire aggregate graph, then discards most of it
var product = await _repository.GetByIdAsync(request.Id, ct);
return new ProductDto(product.Id, product.Name, ...);
```

Problems:

1. Loading an aggregate through `IRepository<T>` uses EF Core change tracking. Every property on
   the entity is loaded even if the DTO only needs three of them.
2. If the aggregate has navigation properties (owned entities, collections), EF Core may issue
   additional queries. The N+1 problem manifests here.
3. The aggregate is loaded into memory, partially read, then immediately discarded. The CPU and
   allocation cost is wasted.
4. Kills AOT compatibility for the same reasons as injecting `KunDbContext` directly.

Write the SQL to return only the columns you need. If the SQL feels verbose, that verbosity is
honest — it reflects exactly what data is fetched.

---

## AOT Note: [DapperAot] Annotation

Dapper's classic `Query<T>` uses runtime reflection to build the object reader for `T`. In an
AOT-published binary, reflection is not available for types that were not preserved by the IL
linker, and the reader code is not generated.

The `[DapperAot]` attribute (from `Dapper.AOT`) instructs Dapper's source generator to emit the
reader code at compile time instead. The generated code is included in the binary at publish time
with zero reflection at runtime.

To use it, annotate the query method:

```csharp
using Dapper;

public sealed class GetProductByIdQueryHandler(IReadDbConnectionFactory connectionFactory)
    : IRequestHandler<GetProductByIdQuery, ProductDto?>
{
    [DapperAot]
    public async ValueTask<ProductDto?> Handle(
        GetProductByIdQuery request,
        CancellationToken cancellationToken)
    {
        using var connection = await connectionFactory.OpenAsync(cancellationToken);
        // ... SQL ...
        return await connection.QuerySingleOrDefaultAsync<ProductDto>(sql, new { request.Id });
    }
}
```

The Dapper AOT source generator scans for `[DapperAot]` and generates the reader for `ProductDto`
at build time. You will see the generated files under `obj/` after `dotnet build`.

If you forget `[DapperAot]` in an AOT-candidate project, the build will succeed but `dotnet
publish -p:PublishAot=true /warnaserror` will fail with a trim analysis warning indicating that
the reflection-based Dapper path is not trim-safe. This is the intended CI gate — the warning
catches missing annotations before deployment.

Ensure that the DTO record type is also AOT-visible. If the DTO is not referenced anywhere
statically (e.g. it is constructed only via Dapper reflection), add it to a
`[JsonSerializable(typeof(MyDto))]` context or reference it explicitly. In practice, returning the
DTO from a handler method counts as a static reference and the linker preserves it.
