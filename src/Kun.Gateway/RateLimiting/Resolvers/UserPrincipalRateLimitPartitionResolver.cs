namespace Kun.Gateway.RateLimiting.Resolvers;

/// <summary>
/// Resolves the rate-limit partition key from the authenticated user's <c>sub</c> claim
/// (the per-user identifier in S2S JWTs and most OIDC tokens). Falls back to <c>"anonymous"</c>
/// when the claim is absent — useful when an open endpoint should still rate-limit per
/// caller IP via a different resolver chain.
/// </summary>
/// <remarks>
/// Pair with <c>AddKunRateLimiter</c> when each authenticated user (not each tenant) should
/// get an independent rate-limit bucket — typical for free-tier APIs where rate limits are
/// per-account rather than per-organization.
/// </remarks>
public sealed class UserPrincipalRateLimitPartitionResolver : IRateLimitPartitionResolver
{
    private const string AnonymousKey = "anonymous";
    private const string SubClaimType = "sub";

    /// <inheritdoc />
    public string ResolvePartitionKey(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var sub = context.User.FindFirst(SubClaimType)?.Value;
        return string.IsNullOrWhiteSpace(sub) ? AnonymousKey : sub;
    }
}
