using System.Linq.Expressions;
using System.Text.Json;

namespace Kun.Authorization;

/// <summary>统一授权主体；权限集合不从令牌直接推导。</summary>
public sealed record AuthorizationSubject(string Id, string? TenantId, string Kind = "user");
/// <summary>权限点与可选资源实例；Permission 使用稳定的资源动作编码。</summary>
public sealed record PermissionRequest(AuthorizationSubject Subject, string Permission, string? ResourceId = null);
/// <summary>可解释判定；Allow=false 是缺省语义。</summary>
public sealed record PermissionDecision(bool Allow, string Reason, IReadOnlyList<string> MatchedPolicies);
/// <summary>权限判定端口；框架不提供 RBAC、ABAC 或持久化引擎。</summary>
public interface IPermissionChecker
{
    /// <summary>判定单一动作。</summary>
    ValueTask<PermissionDecision> CheckAsync(PermissionRequest request, CancellationToken cancellationToken = default);
    /// <summary>批量判定，结果与输入顺序一致。</summary>
    ValueTask<IReadOnlyList<PermissionDecision>> BatchCheckAsync(IReadOnlyList<PermissionRequest> requests, CancellationToken cancellationToken = default);
    /// <summary>列出指定资源的可执行动作。</summary>
    ValueTask<IReadOnlyList<string>> ListAllowedActionsAsync(AuthorizationSubject subject, string resourceType, string? resourceId = null, CancellationToken cancellationToken = default);
    /// <summary>查询主体权限点，供受控目录过滤使用。</summary>
    ValueTask<IReadOnlyList<string>> ListPermissionsAsync(AuthorizationSubject subject, CancellationToken cancellationToken = default);
    /// <summary>返回可解释判定。</summary>
    ValueTask<PermissionDecision> ExplainAsync(PermissionRequest request, CancellationToken cancellationToken = default);
}
/// <summary>可序列化的字段谓词 AST，SQL 编译与参数绑定由 Provider 实现。</summary>
public sealed record DataPermissionPredicate(string Operator, string? Field, JsonElement? Value, IReadOnlyList<DataPermissionPredicate> Children);
/// <summary>本地数据过滤端口，不依赖 EF 包或具体数据库。</summary>
public interface IDataPermissionFilter
{
    /// <summary>获取实体谓词；Provider 负责维持租户隔离与行级语义。</summary>
    ValueTask<Expression<Func<T, bool>>> GetExpressionAsync<T>(PermissionRequest request, CancellationToken cancellationToken = default);
    /// <summary>获取参数化 SQL 编译器使用的谓词 AST。</summary>
    ValueTask<DataPermissionPredicate> GetPredicateAsync(PermissionRequest request, CancellationToken cancellationToken = default);
}
/// <summary>字段可读、可写与需要脱敏的集合。</summary>
public sealed record FieldDecision(IReadOnlyList<string> Readable, IReadOnlyList<string> Writable, IReadOnlyList<string> Masked);
/// <summary>字段权限端口；输出给服务端投影与写入白名单。</summary>
public interface IFieldPolicy
{
    /// <summary>判定请求中的候选字段。</summary>
    ValueTask<FieldDecision> EvaluateAsync(PermissionRequest request, IReadOnlyList<string> fields, CancellationToken cancellationToken = default);
}
/// <summary>命令在编译期声明全部所需权限，避免从用户提交的 JSON 改写权限点。</summary>
public interface IPermissionProtectedRequest
{
    /// <summary>所有声明均需允许才执行 handler。</summary>
    static abstract IReadOnlyList<string> RequiredPermissions { get; }
}
/// <summary>HTTP/gRPC 端点的静态权限元数据。</summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public sealed class RequiresPermissionAttribute(string permission) : Attribute
{
    /// <summary>端点要求的权限点。</summary>
    public string Permission { get; } = permission;
}
