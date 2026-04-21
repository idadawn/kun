using Kun.Core.MultiTenancy;
using Kun.EntityFrameworkCore.DependencyInjection;
using Kun.EntityFrameworkCore.Interceptors;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Kun.EntityFrameworkCore.Tests;

public sealed class TenantRlsInterceptorTests
{
    [Fact]
    public void Ctor_Null_TenantContext_Throws_ArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(
            () => new TenantRlsTransactionInterceptor(tenantContext: null!));
    }

    [Fact]
    public void AddKunTenantRls_Registers_Interceptor_As_Scoped_In_Di()
    {
        var services = new ServiceCollection();
        services.AddScoped<ITenantContext>(_ => NullTenantContext.Instance);

        services.AddKunTenantRls();

        using var sp = services.BuildServiceProvider();
        using var scope1 = sp.CreateScope();
        var a = scope1.ServiceProvider.GetService<TenantRlsTransactionInterceptor>();
        var b = scope1.ServiceProvider.GetService<TenantRlsTransactionInterceptor>();
        Assert.NotNull(a);
        Assert.Same(a, b);

        using var scope2 = sp.CreateScope();
        var c = scope2.ServiceProvider.GetService<TenantRlsTransactionInterceptor>();
        Assert.NotNull(c);
        Assert.NotSame(a, c);
    }

    [Fact]
    public void AddKunTenantRls_Null_Services_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => ((IServiceCollection)null!).AddKunTenantRls());
    }
}
