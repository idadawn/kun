using Microsoft.AspNetCore.Http;

namespace Kun.MultiTenancy.Abstractions;

/// <summary>
/// Produces the current tenant id for an inbound HTTP request. The
/// framework ships <c>DefaultTenantResolver</c>, an options-driven
/// implementation that consults JWT claims, request headers, and a
/// configured fallback. Consumers can replace the default with a
/// custom resolver (subdomain-based, query-string-based, etc.) by
/// registering their own <see cref="ITenantResolver"/> after calling
/// <c>AddKunMultiTenancy</c>.
/// </summary>
public interface ITenantResolver
{
    /// <summary>
    /// Returns the tenant id for <paramref name="httpContext"/>, or
    /// <see langword="null"/> when the resolver cannot determine one.
    /// <see cref="Configuration.KunMultiTenancyOptions.FallbackTenantId"/>
    /// applies last — the default resolver itself emits that when
    /// every configured source comes up empty.
    /// </summary>
    ValueTask<string?> ResolveTenantIdAsync(HttpContext httpContext);
}
