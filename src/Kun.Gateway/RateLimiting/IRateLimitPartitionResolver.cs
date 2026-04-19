namespace Kun.Gateway.RateLimiting;

/// <summary>
/// Resolves the rate-limit partition key for an incoming HTTP request.
/// Implement this interface to plug in a custom partitioning strategy (by tenant, IP, API key, etc.).
/// </summary>
public interface IRateLimitPartitionResolver
{
    /// <summary>
    /// Returns a string partition key that identifies the rate-limit bucket for the given request.
    /// </summary>
    /// <param name="context">The current HTTP context.</param>
    /// <returns>A non-null partition key string.</returns>
    string ResolvePartitionKey(HttpContext context);
}
