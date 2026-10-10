using Grpc.Core;
using Grpc.Core.Interceptors;
using Kun.Core.Results;

namespace Kun.Grpc.Interceptors;

/// <summary>
/// gRPC server interceptor that catches <see cref="ResultFailureException"/> thrown by
/// <c>Result&lt;T&gt;.EnsureSuccess()</c> in any RPC handler and translates the carried
/// <see cref="Error"/> into the appropriate <see cref="RpcException"/>.
/// </summary>
internal sealed class KunResultToRpcInterceptor : Interceptor
{
    /// <inheritdoc />
    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        try
        {
            return await continuation(request, context).ConfigureAwait(false);
        }
        catch (ResultFailureException ex)
        {
            throw ToRpcException(ex.Error);
        }
    }

    /// <inheritdoc />
    public override Task ServerStreamingServerHandler<TRequest, TResponse>(TRequest request, IServerStreamWriter<TResponse> responseStream, ServerCallContext context, ServerStreamingServerMethod<TRequest, TResponse> continuation)
        => MapAsync(() => continuation(request, responseStream, context));

    /// <inheritdoc />
    public override async Task<TResponse> ClientStreamingServerHandler<TRequest, TResponse>(IAsyncStreamReader<TRequest> requestStream, ServerCallContext context, ClientStreamingServerMethod<TRequest, TResponse> continuation)
    {
        try { return await continuation(requestStream, context).ConfigureAwait(false); }
        catch (ResultFailureException error) { throw ToRpcException(error.Error); }
    }

    /// <inheritdoc />
    public override Task DuplexStreamingServerHandler<TRequest, TResponse>(IAsyncStreamReader<TRequest> requestStream, IServerStreamWriter<TResponse> responseStream, ServerCallContext context, DuplexStreamingServerMethod<TRequest, TResponse> continuation)
        => MapAsync(() => continuation(requestStream, responseStream, context));

    private static async Task MapAsync(Func<Task> next)
    {
        try { await next().ConfigureAwait(false); }
        catch (ResultFailureException error) { throw ToRpcException(error.Error); }
    }

    /// <summary>
    /// Maps a <see cref="Error"/> to an <see cref="RpcException"/> with the appropriate
    /// <see cref="StatusCode"/>. The error <see cref="Error.Code"/> is included in the
    /// response trailers as <c>kun-error-code</c>.
    /// </summary>
    /// <param name="error">The error to map.</param>
    /// <returns>An <see cref="RpcException"/> with the mapped status code.</returns>
    public static RpcException ToRpcException(Error error)
    {
        var statusCode = MapErrorType(error.Type);
        var trailers = new Metadata
        {
            { "kun-error-code", error.Code }
        };
        return new RpcException(new Status(statusCode, error.Message), trailers);
    }

    /// <summary>
    /// Maps an <see cref="ErrorType"/> to the corresponding gRPC <see cref="StatusCode"/>.
    /// </summary>
    /// <param name="errorType">The error type to map.</param>
    /// <returns>The gRPC status code.</returns>
    public static StatusCode MapErrorType(ErrorType errorType) => errorType switch
    {
        ErrorType.Validation => StatusCode.InvalidArgument,
        ErrorType.NotFound => StatusCode.NotFound,
        ErrorType.Conflict => StatusCode.FailedPrecondition,
        ErrorType.Unauthorized => StatusCode.Unauthenticated,
        ErrorType.Forbidden => StatusCode.PermissionDenied,
        ErrorType.Unavailable => StatusCode.Unavailable,
        _ => StatusCode.Internal
    };
}
