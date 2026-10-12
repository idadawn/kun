using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;

namespace Kun.Security.Authentication;

/// <summary>从已认证的主身份读取授权主体，不拼接不同身份的 claim。</summary>
public static class PrincipalClaims
{
    /// <summary>读取 sub/NameIdentifier、tenant_id 与 principal_kind；缺少类型的旧浏览器身份按 user，重复或冲突字段拒绝。</summary>
    public static bool TryRead(ClaimsPrincipal? principal, [NotNullWhen(true)] out string? subjectId,
        out string? tenantId, [NotNullWhen(true)] out string? principalKind)
    {
        subjectId = null; tenantId = null; principalKind = null;
        if (principal?.Identity is not ClaimsIdentity { IsAuthenticated: true } identity) return false;
        var identifiers = identity.Claims.Where(claim => claim.Type is "sub" or ClaimTypes.NameIdentifier)
            .Select(claim => claim.Value).Distinct(StringComparer.Ordinal).ToArray();
        if (identifiers.Length != 1 || string.IsNullOrWhiteSpace(identifiers[0])) return false;
        var kinds = identity.FindAll("principal_kind").ToArray();
        var kind = kinds.Length == 0 ? "user" : kinds.Length == 1 ? kinds[0].Value : null;
        if (kind is not ("user" or "service" or "agent")) return false;
        var tenants = identity.FindAll("tenant_id").ToArray();
        if (tenants.Length > 1) return false;
        subjectId = identifiers[0]; principalKind = kind;
        tenantId = tenants.Length == 0 || tenants[0].ValueType == "JSON_NULL" ? null : tenants[0].Value;
        return true;
    }
}
