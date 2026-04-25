using FluentAssertions;
using Kun.Core.MultiTenancy;
using Xunit;

namespace Kun.Core.Tests.MultiTenancy;

/// <summary>
/// Guard-clause coverage for <see cref="TenantInfo"/>. The record is a DTO crossing module
/// boundaries so the ArgumentException contract is load-bearing.
/// </summary>
public sealed class TenantInfoTests
{
    [Fact]
    public void Ctor_AcceptsNonEmptyTenantId()
    {
        var info = new TenantInfo("tenant-contoso");

        info.TenantId.Should().Be("tenant-contoso");
        info.DisplayName.Should().BeNull();
    }

    [Fact]
    public void Ctor_AcceptsOptionalDisplayName()
    {
        var info = new TenantInfo("tenant-fabrikam", displayName: "Fabrikam Inc.");

        info.DisplayName.Should().Be("Fabrikam Inc.");
    }

    [Fact]
    public void Ctor_RejectsNullTenantId()
    {
        var act = () => new TenantInfo(null!);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("tenantId");
    }

    [Fact]
    public void Ctor_RejectsEmptyTenantId()
    {
        var act = () => new TenantInfo(string.Empty);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("tenantId");
    }

    [Fact]
    public void Ctor_RejectsWhitespaceTenantId()
    {
        var act = () => new TenantInfo("   ");

        act.Should().Throw<ArgumentException>()
            .WithParameterName("tenantId");
    }
}
