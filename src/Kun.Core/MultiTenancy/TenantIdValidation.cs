namespace Kun.Core.MultiTenancy;

/// <summary>HTTP、消息与后台任务共用的租户标识白名单。</summary>
public static class TenantIdValidation
{
    /// <summary>只接受 1–128 个 ASCII 字母、数字、点、下划线或连字符。</summary>
    public static bool IsValid(string? tenantId) =>
        tenantId is { Length: > 0 and <= 128 } &&
        tenantId.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-');
}
