namespace Kun.Gateway.RateLimiting.Resolvers;

/// <summary>
/// Resolves the rate-limit partition key from the remote IP address.
/// Handles <c>X-Forwarded-For</c> when the gateway is behind a reverse proxy or load balancer.
/// </summary>
public sealed class IpRateLimitPartitionResolver : IRateLimitPartitionResolver
{
    /// <inheritdoc />
    public string ResolvePartitionKey(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Honour X-Forwarded-For when present (first address is the originating client)
        var forwarded = context.Request.Headers["X-Forwarded-For"].ToString();
        if (!string.IsNullOrWhiteSpace(forwarded))
        {
            var first = forwarded.Split(',')[0].Trim();
            if (!string.IsNullOrWhiteSpace(first))
                return first;
        }

        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}
