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
    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        var httpContext = context.GetHttpContext();
        var user = httpContext.User;

        if (user?.Identity?.IsAuthenticated != true)
        {
            throw new RpcException(new Status(StatusCode.Unauthenticated, "Request is not authenticated."));
        }

        var tenantId = user.FindFirst(TenantIdClaimType)?.Value;
        if (tenantId is null)
        {
            throw new RpcException(new Status(StatusCode.Unauthenticated, "Missing required claim: tenant_id."));
        }

        return await continuation(request, context).ConfigureAwait(false);
    }
}
