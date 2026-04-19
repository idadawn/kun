using System.Diagnostics;
using Grpc.Core;
using Grpc.Core.Interceptors;

namespace Kun.Grpc.Interceptors;

/// <summary>
/// gRPC server interceptor that starts a diagnostic <see cref="Activity"/> per unary RPC call,
/// tagged with the method name and <c>rpc.system=grpc</c>.
/// Uses <see cref="ActivitySource"/> named <c>Kun.Grpc</c>.
/// </summary>
internal sealed class KunTracingInterceptor : Interceptor
{
    /// <summary>The <see cref="ActivitySource"/> used by this interceptor.</summary>
    public static readonly ActivitySource ActivitySource = new("Kun.Grpc");

    /// <inheritdoc />
    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        using var activity = ActivitySource.StartActivity("grpc.server", ActivityKind.Server);

        if (activity is not null)
        {
            activity.SetTag("rpc.system", "grpc");
            activity.SetTag("rpc.method", context.Method);
        }

        return await continuation(request, context).ConfigureAwait(false);
    }
}
