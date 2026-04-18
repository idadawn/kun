using Kun.Core.Cqrs;

namespace Kun.Sample.Catalog.Application.Queries;

/// <summary>Returns a single product by its ID, or <see langword="null"/> if not found.</summary>
public sealed record GetProductByIdQuery(Guid Id) : IQuery<ProductDto?>;
