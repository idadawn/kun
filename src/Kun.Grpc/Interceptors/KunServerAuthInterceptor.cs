using Grpc.Core;
using Grpc.Core.Interceptors;

namespace Kun.Grpc.Interceptors;

/// <summary>
/// gRPC server interceptor that enforces authenticated access for multi-tenant calls.
/// Validates that the request has an authenticated <see cref="System.Security.Claims.ClaimsPrincipal"/>
/// with a <c>tenant_id</c> claim. Tenant/user context population is handled by
/// <c>Kun.Security</c>'s scoped <c>JwtTenantContext</c>/<c>JwtCurrentUser</c>.
/// </summary>
internal sealed class KunServerAuthInterceptor : Interceptor
{
    private const string TenantIdClaimType = "tenant_id";

    /// <inheritdoc />
    public override Task<TResponse> UnaryServerHandler<TRequest, TResponse>(TRequest request, ServerCallContext context, UnaryServerMethod<TRequest, TResponse> continuation)
    {
        Authorize(context);
        return continuation(request, context);
    }

    /// <inheritdoc />
    public override Task ServerStreamingServerHandler<TRequest, TResponse>(TRequest request, IServerStreamWriter<TResponse> responseStream, ServerCallContext context, ServerStreamingServerMethod<TRequest, TResponse> continuation)
    {
        Authorize(context);
        return continuation(request, responseStream, context);
    }

    /// <inheritdoc />
    public override Task<TResponse> ClientStreamingServerHandler<TRequest, TResponse>(IAsyncStreamReader<TRequest> requestStream, ServerCallContext context, ClientStreamingServerMethod<TRequest, TResponse> continuation)
    {
        Authorize(context);
        return continuation(requestStream, context);
    }

    /// <inheritdoc />
    public override Task DuplexStreamingServerHandler<TRequest, TResponse>(IAsyncStreamReader<TRequest> requestStream, IServerStreamWriter<TResponse> responseStream, ServerCallContext context, DuplexStreamingServerMethod<TRequest, TResponse> continuation)
    {
        Authorize(context);
        return continuation(requestStream, responseStream, context);
    }

    private static void Authorize(ServerCallContext context)
    {
        var user = context.GetHttpContext().User;
        if (user?.Identity?.IsAuthenticated != true)
            throw new RpcException(new Status(StatusCode.Unauthenticated, "Request is not authenticated."));
        if (string.IsNullOrWhiteSpace(user.FindFirst(TenantIdClaimType)?.Value))
            throw new RpcException(new Status(StatusCode.Unauthenticated, "Missing required claim: tenant_id."));
    }
}
