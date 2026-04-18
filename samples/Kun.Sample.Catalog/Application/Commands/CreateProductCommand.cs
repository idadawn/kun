using Kun.Core.Cqrs;

namespace Kun.Sample.Catalog.Application.Commands;

/// <summary>Creates a new catalog product and returns its generated ID.</summary>
public sealed record CreateProductCommand(
    string Name,
    string Description,
    decimal Price,
    Guid CategoryId,
    int InitialStock = 0) : ICommand<Guid>;
