namespace Kun.MultiTenancy.Configuration;

/// <summary>Options for <c>AddKunMultiTenancy</c>.</summary>
public sealed class KunMultiTenancyOptions
{
    /// <summary>生产模式仅接受已认证身份的 tenant_id，拒绝请求头与默认租户回退。</summary>
    public bool Production { get; set; }

    /// <summary>JWT claim names consulted in order by <c>DefaultTenantResolver</c>. Add via <see cref="UseJwtClaim"/>.</summary>
    public List<string> JwtClaimNames { get; } = ["tenant_id"];

    /// <summary>HTTP header names consulted in order by <c>DefaultTenantResolver</c>. Add via <see cref="UseHttpHeader"/>.</summary>
    public List<string> HttpHeaderNames { get; } = [];

    /// <summary>
    /// Tenant id returned when no configured source yields a value.
    /// Defaults to <c>"default"</c> — flip to <see langword="null"/> if
    /// the consumer wants anonymous requests to fall out of
    /// multi-tenant-filtered queries instead of being routed to a
    /// shared "default" bucket.
    /// </summary>
    public string? FallbackTenantId { get; set; } = "default";

    /// <summary>Appends <paramref name="claimName"/> to the JWT-claim lookup chain.</summary>
    public KunMultiTenancyOptions UseJwtClaim(string claimName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(claimName);
        JwtClaimNames.Add(claimName);
        return this;
    }

    /// <summary>Appends <paramref name="headerName"/> to the HTTP-header lookup chain.</summary>
    public KunMultiTenancyOptions UseHttpHeader(string headerName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(headerName);
        HttpHeaderNames.Add(headerName);
        return this;
    }
}
