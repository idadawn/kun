namespace Kun.Gateway.RateLimiting.Resolvers;

/// <summary>
/// Resolves the rate-limit partition key from the remote IP address.
/// Reads the peer normalized by trusted ForwardedHeadersMiddleware; never parses raw headers.
/// </summary>
public sealed class IpRateLimitPartitionResolver : IRateLimitPartitionResolver
{
    /// <inheritdoc />
    public string ResolvePartitionKey(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // 只有 ForwardedHeadersMiddleware 可以在验证可信代理后更新 RemoteIpAddress。
        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}
