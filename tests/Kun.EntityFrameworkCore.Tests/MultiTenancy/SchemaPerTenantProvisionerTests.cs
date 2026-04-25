using Kun.Core.MultiTenancy;
using Kun.EntityFrameworkCore.MultiTenancy;
using Microsoft.Extensions.Options;
using Xunit;

namespace Kun.EntityFrameworkCore.Tests.MultiTenancy;

public sealed class SchemaPerTenantProvisionerTests
{
    [Fact]
    public void Ctor_NullFactory_Throws()
    {
        var options = Options.Create(new KunMultiTenancyOptions());
        Assert.Throws<ArgumentNullException>(() =>
            new SchemaPerTenantProvisioner<TestDbContext>(factory: null!, options));
    }

    [Fact]
    public void Ctor_NullOptions_Throws()
    {
        var factory = new FakeFactory();
        Assert.Throws<ArgumentNullException>(() =>
            new SchemaPerTenantProvisioner<TestDbContext>(factory, options: null!));
    }

    [Fact]
    public async Task ProvisionAsync_RejectsUnsafeSchemaChars()
    {
        var provisioner = BuildProvisioner(new KunMultiTenancyOptions());

        Task act() => provisioner.ProvisionAsync(
            new TenantInfo("bad; DROP TABLE users--"),
            TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<ArgumentException>(act);
    }

    [Fact]
    public async Task DeprovisionAsync_ThrowsWhenDestructiveOptInDisabled()
    {
        var provisioner = BuildProvisioner(new KunMultiTenancyOptions { AllowDestructiveDeprovision = false });

        Task act() => provisioner.DeprovisionAsync("tenant-a", TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<NotSupportedException>(act);
    }

    [Fact]
    public async Task DeprovisionAsync_RejectsUnsafeSchemaChars_EvenWhenOptInEnabled()
    {
        var provisioner = BuildProvisioner(new KunMultiTenancyOptions { AllowDestructiveDeprovision = true });

        Task act() => provisioner.DeprovisionAsync("bad name!", TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<ArgumentException>(act);
    }

    private static SchemaPerTenantProvisioner<TestDbContext> BuildProvisioner(KunMultiTenancyOptions opts)
    {
        var factory = new FakeFactory();
        return new SchemaPerTenantProvisioner<TestDbContext>(factory, Options.Create(opts));
    }

    private sealed class FakeFactory : ITenantDbContextFactory<TestDbContext>
    {
        public TestDbContext CreateForTenant(string tenantId)
        {
            // Test never reaches CreateForTenant because all current test cases assert on
            // argument validation BEFORE the factory is called.
            throw new InvalidOperationException("Fake factory should not be called in unit tests.");
        }
    }
}
