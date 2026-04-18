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

        const string sql = """
            SELECT id         AS "Id",
                   name       AS "Name",
                   description AS "Description",
                   price      AS "Price",
                   category_id AS "CategoryId",
                   stock      AS "Stock"
            FROM   catalog_products
            WHERE  id = @Id
              AND  is_deleted = false
            """;

        var row = await connection.QuerySingleOrDefaultAsync<ProductDto>(
            sql,
            new { request.Id });

        return row;
    }
}
