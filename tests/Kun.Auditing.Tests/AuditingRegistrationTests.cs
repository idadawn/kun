using Kun.Auditing.DependencyInjection;
using Kun.Core.MultiTenancy;
using Kun.Core.Security;
using Kun.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Kun.Auditing.Tests;

// 实体审计不占租户与当前主体端口：安全与多租户组件在它之前或之后以 TryAdd 登记都生效。
public sealed class AuditingRegistrationTests
{
    [Fact]
    public void AddKunAuditing_DoesNotRegisterTenantOrCurrentUser()
    {
        var services = new ServiceCollection();
        services.AddKunAuditing<AuditedContext>();
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(ITenantContext));
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(ICurrentUser));
    }

    [Fact]
    public void Interceptor_ResolvesWithoutTenantOrCurrentUser()
    {
        var services = new ServiceCollection();
        services.AddKunAuditing<AuditedContext>();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<TransactionalAuditingSaveChangesInterceptor>());
    }

    private sealed class AuditedContext(DbContextOptions<AuditedContext> options, ITenantContext tenant) : KunDbContext(options, tenant);
}
