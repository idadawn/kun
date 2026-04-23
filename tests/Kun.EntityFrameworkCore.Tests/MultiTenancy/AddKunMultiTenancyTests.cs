using Kun.Core.MultiTenancy;
using Kun.EntityFrameworkCore.Interceptors;
using Kun.EntityFrameworkCore.MultiTenancy;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Kun.EntityFrameworkCore.Tests.MultiTenancy;

public sealed class AddKunMultiTenancyTests
{
    [Fact]
    public void NullServices_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            ((IServiceCollection)null!).AddKunMultiTenancy<TestDbContext>(_ => { }));
    }

    [Fact]
    public void NullConfigure_Throws()
    {
        var services = new ServiceCollection();
        Assert.Throws<ArgumentNullException>(() =>
            services.AddKunMultiTenancy<TestDbContext>(configure: null!));
    }

    [Fact]
    public void SharedStrategy_RegistersRlsInterceptorAndNullProvisioner()
    {
        var services = new ServiceCollection();
        services.AddScoped<ITenantContext>(_ => NullTenantContext.Instance);

        services.AddKunMultiTenancy<TestDbContext>(o => o.Strategy = TenantIsolationStrategy.Shared);

        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetService<TenantRlsTransactionInterceptor>());
        var provisioner = scope.ServiceProvider.GetService<ITenantProvisioner>();
        Assert.IsType<NullTenantProvisioner>(provisioner);
    }

    [Fact]
    public void SchemaPerTenantStrategy_RegistersFactoryRlsAndProvisioner()
    {
        var services = new ServiceCollection();
        services.AddScoped<ITenantContext>(_ => NullTenantContext.Instance);
        // Consumer provides their own tenant DbContext factory.
        services.AddSingleton<ITenantDbContextFactory<TestDbContext>>(new FakeContextFactory());

        services.AddKunMultiTenancy<TestDbContext>(o => o.Strategy = TenantIsolationStrategy.SchemaPerTenant);

        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetService<TenantRlsTransactionInterceptor>());
        Assert.NotNull(sp.GetService<TenantAwareModelCacheKeyFactory>());
        var provisioner = sp.GetService<ITenantProvisioner>();
        Assert.IsType<SchemaPerTenantProvisioner<TestDbContext>>(provisioner);
    }

    [Fact]
    public void DatabasePerTenantStrategy_ThrowsOnResolution()
    {
        var services = new ServiceCollection();
        services.AddKunMultiTenancy<TestDbContext>(o => o.Strategy = TenantIsolationStrategy.DatabasePerTenant);

        using var sp = services.BuildServiceProvider();

        Assert.Throws<NotImplementedException>(() => sp.GetService<ITenantProvisioner>());
    }

    private sealed class FakeContextFactory : ITenantDbContextFactory<TestDbContext>
    {
        public TestDbContext CreateForTenant(string tenantId)
            => throw new InvalidOperationException("Fake; not expected to be called in DI registration test.");
    }
}
