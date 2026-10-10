using System.Diagnostics;
using Grpc.Core;
using Grpc.Core.Interceptors;

namespace Kun.Grpc.Interceptors;

/// <summary>覆盖四种 RPC 形态，Activity 生命周期持续到最后一个异步流操作结束。</summary>
internal sealed class KunTracingInterceptor : Interceptor
{
    /// <summary>框架 gRPC 追踪源。</summary>
    public static readonly ActivitySource ActivitySource = new("Kun.Grpc");

    /// <inheritdoc />
    public override Task<TResponse> UnaryServerHandler<TRequest, TResponse>(TRequest request, ServerCallContext context, UnaryServerMethod<TRequest, TResponse> continuation)
        => TraceAsync(context, "unary", () => continuation(request, context));

    /// <inheritdoc />
    public override Task ServerStreamingServerHandler<TRequest, TResponse>(TRequest request, IServerStreamWriter<TResponse> responseStream, ServerCallContext context, ServerStreamingServerMethod<TRequest, TResponse> continuation)
        => TraceAsync(context, "server_streaming", () => continuation(request, responseStream, context));

    /// <inheritdoc />
    public override Task<TResponse> ClientStreamingServerHandler<TRequest, TResponse>(IAsyncStreamReader<TRequest> requestStream, ServerCallContext context, ClientStreamingServerMethod<TRequest, TResponse> continuation)
        => TraceAsync(context, "client_streaming", () => continuation(requestStream, context));

    /// <inheritdoc />
    public override Task DuplexStreamingServerHandler<TRequest, TResponse>(IAsyncStreamReader<TRequest> requestStream, IServerStreamWriter<TResponse> responseStream, ServerCallContext context, DuplexStreamingServerMethod<TRequest, TResponse> continuation)
        => TraceAsync(context, "duplex_streaming", () => continuation(requestStream, responseStream, context));

    private static async Task TraceAsync(ServerCallContext context, string shape, Func<Task> next)
        => await TraceAsync<object?>(context, shape, async () => { await next().ConfigureAwait(false); return null; }).ConfigureAwait(false);

    private static async Task<T> TraceAsync<T>(ServerCallContext context, string shape, Func<Task<T>> next)
    {
        using var activity = ActivitySource.StartActivity("grpc.server", ActivityKind.Server);
        activity?.SetTag("rpc.system", "grpc");
        activity?.SetTag("rpc.method", context.Method);
        activity?.SetTag("rpc.type", shape);
        try
        {
            var result = await next().ConfigureAwait(false);
            activity?.SetTag("rpc.grpc.status_code", (int)context.Status.StatusCode);
            return result;
        }
        catch (Exception error)
        {
            var code = error is RpcException rpc ? rpc.StatusCode
                : error is OperationCanceledException && context.CancellationToken.IsCancellationRequested ? StatusCode.Cancelled : StatusCode.Internal;
            activity?.SetTag("rpc.grpc.status_code", (int)code);
            activity?.SetStatus(ActivityStatusCode.Error, code.ToString());
            throw;
        }
    }
}
