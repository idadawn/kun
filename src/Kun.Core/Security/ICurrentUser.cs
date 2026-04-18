namespace Kun.Core.Security;

/// <summary>当前用户上下文，从 S2S JWT 的 sub / roles claim 中解析。</summary>
public interface ICurrentUser
{
    /// <summary>用户唯一标识（通常是 JWT 的 <c>sub</c> claim）。未登录时为 <see langword="null"/>。</summary>
    string? UserId { get; }

    /// <summary>用户当前持有的角色集合。</summary>
    IReadOnlyList<string> Roles { get; }

    /// <summary>是否已认证。</summary>
    bool IsAuthenticated { get; }
}
