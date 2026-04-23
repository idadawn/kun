using FluentAssertions;
using Kun.Core.MultiTenancy;
using Xunit;

namespace Kun.Core.Tests.MultiTenancy;

/// <summary>
/// <see cref="NullTenantProvisioner"/> is the default registered for the Shared strategy;
/// it must be side-effect-free so consumers can rely on it at boot without wiring any
/// tenant lifecycle plumbing.
/// </summary>
public sealed class NullTenantProvisionerTests
{
    [Fact]
    public async Task ProvisionAsync_CompletesWithoutThrowing()
    {
        var provisioner = NullTenantProvisioner.Instance;

        var act = async () => await provisioner.ProvisionAsync(new TenantInfo("t-1"), TestContext.Current.CancellationToken);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DeprovisionAsync_CompletesWithoutThrowing()
    {
        var provisioner = NullTenantProvisioner.Instance;

        var act = async () => await provisioner.DeprovisionAsync("t-1", TestContext.Current.CancellationToken);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public void Instance_IsSingleton()
    {
        NullTenantProvisioner.Instance.Should().BeSameAs(NullTenantProvisioner.Instance);
    }
}
