namespace Kun.Gateway.RateLimiting.Resolvers;

/// <summary>
/// Resolves the rate-limit partition key from the <c>tenant_id</c> claim of the authenticated user.
/// Falls back to <c>"anonymous"</c> when the claim is absent.
/// </summary>
public sealed class TenantRateLimitPartitionResolver : IRateLimitPartitionResolver
{
    private const string AnonymousKey = "anonymous";

    /// <inheritdoc />
    public string ResolvePartitionKey(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var tenantId = context.User.FindFirst("tenant_id")?.Value;
        return string.IsNullOrWhiteSpace(tenantId) ? AnonymousKey : tenantId;
    }
}
