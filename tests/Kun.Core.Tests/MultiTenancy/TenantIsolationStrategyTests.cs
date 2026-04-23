using FluentAssertions;
using Kun.Core.MultiTenancy;
using Xunit;

namespace Kun.Core.Tests.MultiTenancy;

/// <summary>
/// Pins the <see cref="TenantIsolationStrategy"/> enum contract: exactly three values in the
/// documented order (Shared=0, SchemaPerTenant=1, DatabasePerTenant=2). Downstream code and
/// docs depend on the numeric values (e.g. appsettings JSON bindings) and the value count
/// (enum-exhaustive switch expressions).
/// </summary>
public sealed class TenantIsolationStrategyTests
{
    [Fact]
    public void Enum_HasExactlyThreeValues()
    {
        var values = Enum.GetValues<TenantIsolationStrategy>();

        values.Should().HaveCount(3);
    }

    [Fact]
    public void Enum_Values_MatchDocumentedNumericOrder()
    {
        ((int)TenantIsolationStrategy.Shared).Should().Be(0);
        ((int)TenantIsolationStrategy.SchemaPerTenant).Should().Be(1);
        ((int)TenantIsolationStrategy.DatabasePerTenant).Should().Be(2);
    }
}
