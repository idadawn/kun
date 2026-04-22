using FluentAssertions;
using Xunit;

namespace Kun.Auditing.Tests;

public sealed class AuditableEntityRegistryTests
{
    [Fact]
    public void Empty_seed_produces_empty_registered_types()
    {
        var registry = new AuditableEntityRegistry([]);

        registry.RegisteredTypes.Should().BeEmpty();
    }

    [Fact]
    public void Single_type_is_registered()
    {
        var registry = new AuditableEntityRegistry([typeof(FakeEntity)]);

        registry.RegisteredTypes.Should().HaveCount(1);
        registry.RegisteredTypes.Should().Contain(typeof(FakeEntity));
    }

    [Fact]
    public void Multiple_types_are_registered_and_duplicates_collapse()
    {
        var registry = new AuditableEntityRegistry([typeof(FakeEntity), typeof(AnotherEntity), typeof(FakeEntity)]);

        registry.RegisteredTypes.Should().HaveCount(2);
        registry.RegisteredTypes.Should().Contain([typeof(FakeEntity), typeof(AnotherEntity)]);
    }

    [Fact]
    public void Lookup_is_case_sensitive_by_type_identity()
    {
        var registry = new AuditableEntityRegistry([typeof(FakeEntity)]);

        // Different CLR types with the same name (in different namespaces) must not collide.
        registry.RegisteredTypes.Should().NotContain(typeof(Nested.FakeEntity));
    }

    [Fact]
    public void Registered_types_is_frozen_and_not_mutable()
    {
        var registry = new AuditableEntityRegistry([typeof(FakeEntity)]);

        // IReadOnlySet exposes no mutation API — compile-time guarantee. The runtime
        // type should live under the System.Collections.Frozen namespace. The concrete
        // class name depends on set size (FrozenSet / SmallFrozenSet / …); all of them
        // are members of that namespace.
        registry.RegisteredTypes.GetType().Namespace.Should().Be("System.Collections.Frozen");
    }

    private sealed class FakeEntity { }
    private sealed class AnotherEntity { }

    private static class Nested
    {
        public sealed class FakeEntity { }
    }
}
