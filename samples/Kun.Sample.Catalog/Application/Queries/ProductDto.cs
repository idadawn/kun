namespace Kun.Sample.Catalog.Application.Queries;

/// <summary>Flat read-model DTO returned by <see cref="GetProductByIdQuery"/>.</summary>
public sealed record ProductDto(
    Guid Id,
    string Name,
    string Description,
    decimal Price,
    Guid CategoryId,
    int Stock);
