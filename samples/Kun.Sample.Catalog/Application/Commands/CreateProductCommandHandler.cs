using Kun.Core.Cqrs;
using Kun.EntityFrameworkCore;
using Kun.Sample.Catalog.Domain;
using Kun.Sample.Catalog.Infrastructure;

namespace Kun.Sample.Catalog.Application.Commands;

/// <summary>
/// Handles <see cref="CreateProductCommand"/>: persists a new <see cref="Product"/>
/// aggregate and commits the unit-of-work (which atomically writes the outbox row).
/// </summary>
public sealed class CreateProductCommandHandler(
    CatalogDbContext db,
    IUnitOfWork unitOfWork)
    : IRequestHandler<CreateProductCommand, Guid>
{
    /// <inheritdoc/>
    public async ValueTask<Guid> Handle(
        CreateProductCommand request,
        CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();

        var product = Product.Create(
            id,
            request.Name,
            request.Description,
            request.Price,
            request.CategoryId,
            request.InitialStock);

        db.Products.Add(product);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return id;
    }
}
