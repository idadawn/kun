using Grpc.Core;
using Kun.Mediator;
using Kun.Sample.Catalog.Application.Commands;
using Kun.Sample.Catalog.Application.Queries;

namespace Kun.Sample.Catalog.Grpc;

/// <summary>
/// gRPC service implementation for the Catalog bounded context.
/// Exposes <c>GetProduct</c> and <c>ReserveStock</c> RPCs backed by the CQRS mediator.
/// </summary>
public sealed class CatalogGrpcService : CatalogService.CatalogServiceBase
{
    private readonly ISender _sender;

    /// <summary>Initializes a new instance of <see cref="CatalogGrpcService"/>.</summary>
    public CatalogGrpcService(ISender sender) => _sender = sender;

    /// <inheritdoc/>
    public override async Task<GetProductReply> GetProduct(
        GetProductRequest request,
        ServerCallContext context)
    {
        if (!Guid.TryParse(request.ProductId, out var id))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "ProductId must be a GUID"));

        var dto = await _sender.Send(new GetProductByIdQuery(id), context.CancellationToken);
        if (dto is null)
            throw new RpcException(new Status(StatusCode.NotFound, $"Product {id} not found"));

        return new GetProductReply
        {
            ProductId = dto.Id.ToString(),
            Name = dto.Name ?? string.Empty,
            Price = (double)dto.Price,
            Stock = dto.Stock,
        };
    }

    /// <inheritdoc/>
    public override async Task<ReserveStockReply> ReserveStock(
        ReserveStockRequest request,
        ServerCallContext context)
    {
        if (!Guid.TryParse(request.ProductId, out var id))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "ProductId must be a GUID"));

        var result = await _sender.Send(new AdjustStockCommand(id, -request.Quantity), context.CancellationToken);

        if (result.IsFailure)
            return new ReserveStockReply { Success = false, FailureReason = result.Error.Message };

        return new ReserveStockReply { Success = true };
    }
}
