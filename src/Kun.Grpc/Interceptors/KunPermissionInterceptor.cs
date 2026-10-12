using Grpc.Core;
using Grpc.Core.Interceptors;
using Kun.Authorization;
using Kun.Security.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Kun.Grpc.Interceptors;

/// <summary>全部四种 RPC 形态在进入 handler 或读取首帧前检查端点权限元数据。</summary>
internal sealed class KunPermissionInterceptor : Interceptor
{
    private static async Task CheckAsync(ServerCallContext context)
    {
        var http = context.GetHttpContext();
        var requirements = http.GetEndpoint()?.Metadata.GetOrderedMetadata<RequiresPermissionAttribute>();
        if (requirements is null || requirements.Count == 0) return;
        if (!PrincipalClaims.TryRead(http.User, out var id, out var tenant, out var kind))
            throw new RpcException(new Status(StatusCode.Unauthenticated, "请求未认证。"));
        var checker = http.RequestServices.GetService<IPermissionChecker>();
        if (checker is null) throw new RpcException(new Status(StatusCode.PermissionDenied, "权限引擎未配置。"));
        foreach (var requirement in requirements)
        {
            if (string.IsNullOrWhiteSpace(requirement.Permission) ||
                !(await checker.CheckAsync(new PermissionRequest(
                    new AuthorizationSubject(id, tenant, kind), requirement.Permission), context.CancellationToken).ConfigureAwait(false)).Allow)
                throw new RpcException(new Status(StatusCode.PermissionDenied, "不允许执行此操作。"));
        }
    }

    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(TRequest request, ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        await CheckAsync(context).ConfigureAwait(false);
        return await continuation(request, context).ConfigureAwait(false);
    }
    public override async Task ServerStreamingServerHandler<TRequest, TResponse>(TRequest request, IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context, ServerStreamingServerMethod<TRequest, TResponse> continuation)
    {
        await CheckAsync(context).ConfigureAwait(false);
        await continuation(request, responseStream, context).ConfigureAwait(false);
    }
    public override async Task<TResponse> ClientStreamingServerHandler<TRequest, TResponse>(IAsyncStreamReader<TRequest> requestStream,
        ServerCallContext context, ClientStreamingServerMethod<TRequest, TResponse> continuation)
    {
        await CheckAsync(context).ConfigureAwait(false);
        return await continuation(requestStream, context).ConfigureAwait(false);
    }
    public override async Task DuplexStreamingServerHandler<TRequest, TResponse>(IAsyncStreamReader<TRequest> requestStream,
        IServerStreamWriter<TResponse> responseStream, ServerCallContext context, DuplexStreamingServerMethod<TRequest, TResponse> continuation)
    {
        await CheckAsync(context).ConfigureAwait(false);
        await continuation(requestStream, responseStream, context).ConfigureAwait(false);
    }
}
