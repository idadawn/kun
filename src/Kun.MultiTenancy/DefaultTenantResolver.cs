using Kun.MultiTenancy.Abstractions;
using Kun.MultiTenancy.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Kun.MultiTenancy;

/// <summary>
/// Options-driven <see cref="ITenantResolver"/>. Consults the
/// registered JWT claim names against <c>HttpContext.User</c>, then
/// the registered HTTP header names against <c>HttpContext.Request.Headers</c>,
/// and finally <see cref="KunMultiTenancyOptions.FallbackTenantId"/>
/// when every configured source comes up empty.
/// </summary>
public sealed class DefaultTenantResolver : ITenantResolver
{
    private readonly KunMultiTenancyOptions _options;

    /// <summary>Creates a resolver bound to the supplied <paramref name="options"/>.</summary>
    public DefaultTenantResolver(IOptions<KunMultiTenancyOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
    }

    /// <inheritdoc />
    public ValueTask<string?> ResolveTenantIdAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        foreach (var claim in _options.JwtClaimNames)
        {
            var value = httpContext.User.FindFirst(claim)?.Value;
            if (!string.IsNullOrWhiteSpace(value))
            {
                return ValueTask.FromResult<string?>(value);
            }
        }

        foreach (var header in _options.HttpHeaderNames)
        {
            var value = httpContext.Request.Headers[header].ToString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                return ValueTask.FromResult<string?>(value);
            }
        }

        return ValueTask.FromResult(_options.FallbackTenantId);
    }
}
