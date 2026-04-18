using Kun.Core.Cqrs;

namespace Kun.Sample.Catalog.Application.Commands;

/// <summary>Adjusts the stock of a catalog product by <see cref="Delta"/> units.</summary>
public sealed record AdjustStockCommand(Guid ProductId, int Delta) : ICommand;
