using Kun.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kun.AspNetCore.Authorization;

/// <summary>将权限端口接入 ASP.NET Core 策略，不提供权限引擎。</summary>
public static class KunPermissionExtensions
{
    /// <summary>注册策略处理器，宿主另行提供 IPermissionChecker；缺失时缺省拒绝。</summary>
    public static IServiceCollection AddKunAuthorization(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddAuthorization();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAuthorizationHandler, KunPermissionHandler>());
        return services;
    }

    /// <summary>给端点附加静态权限元数据与认证策略，所有声明均需满足。</summary>
    public static TBuilder RequireKunPermission<TBuilder>(this TBuilder builder, string permission)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(permission);
        builder.Add(endpoint => endpoint.Metadata.Add(new RequiresPermissionAttribute(permission)));
        builder.RequireAuthorization(new AuthorizationPolicyBuilder().RequireAuthenticatedUser()
            .AddRequirements(new KunPermissionRequirement(permission)).Build());
        return builder;
    }
}

internal sealed record KunPermissionRequirement(string Permission) : IAuthorizationRequirement;

internal sealed class KunPermissionHandler : AuthorizationHandler<KunPermissionRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, KunPermissionRequirement requirement)
    {
        var subject = context.User.FindFirst("sub")?.Value ?? context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (context.Resource is not HttpContext http || context.User.Identity?.IsAuthenticated != true || string.IsNullOrWhiteSpace(subject))
        {
            context.Fail();
            return;
        }
        var checker = http.RequestServices.GetService<IPermissionChecker>();
        if (checker is null) { context.Fail(); return; }
        var request = new PermissionRequest(new AuthorizationSubject(subject, context.User.FindFirst("tenant_id")?.Value), requirement.Permission);
        if ((await checker.CheckAsync(request, http.RequestAborted).ConfigureAwait(false)).Allow) context.Succeed(requirement);
        else context.Fail();
    }
}
