using Dapper;
using Kun.Core.Cqrs;
using Kun.Dapper;

namespace Kun.Sample.Catalog.Application.Queries;

/// <summary>
/// Read-side handler for <see cref="GetProductByIdQuery"/>.
/// Uses Dapper directly against the read connection — no EF change tracking.
/// </summary>
public sealed class GetProductByIdQueryHandler(IReadDbConnectionFactory connectionFactory)
    : IRequestHandler<GetProductByIdQuery, ProductDto?>
{
    /// <inheritdoc/>
    public async ValueTask<ProductDto?> Handle(
        GetProductByIdQuery request,
        CancellationToken cancellationToken)
    {
        using var connection = await connectionFactory.OpenAsync(cancellationToken);

        // Schema-qualify — CatalogDbContext.DefaultSchema = "catalog", so the table
        // lives at "catalog"."catalog_products", and unqualified lookups depend on
        // search_path being set to include the schema (the read connection doesn't
        // get that).
        const string sql = """
            SELECT "Id",
                   "Name",
                   "Description",
                   "Price",
                   "CategoryId",
                   "Stock"
            FROM   "catalog"."catalog_products"
            WHERE  "Id" = @Id
              AND  "IsDeleted" = false
            """;

        var row = await connection.QuerySingleOrDefaultAsync<ProductDto>(
            sql,
            new { request.Id });

        return row;
    }
}
