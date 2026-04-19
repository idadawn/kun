using FluentAssertions;
using Kun.Core.Domain;
using Xunit;

namespace Kun.Core.Tests.Domain;

file sealed class TestEntity : Entity<Guid>
{
    public void SetId(Guid id) => Id = id;
}

file sealed class OtherEntity : Entity<Guid>
{
    public void SetId(Guid id) => Id = id;
}

public class EntityTests
{
    [Fact]
    public void Equals_SameId_ReturnsTrue()
    {
        // Arrange
        var id = Guid.NewGuid();
        var a = new TestEntity();
        var b = new TestEntity();
        a.SetId(id);
        b.SetId(id);

        // Act
        var result = a.Equals(b);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void Equals_DifferentIds_ReturnsFalse()
    {
        // Arrange
        var a = new TestEntity();
        var b = new TestEntity();
        a.SetId(Guid.NewGuid());
        b.SetId(Guid.NewGuid());

        // Act
        var result = a.Equals(b);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void Equals_NullOther_ReturnsFalse()
    {
        // Arrange
        var entity = new TestEntity();
        entity.SetId(Guid.NewGuid());

        // Act
        var result = entity.Equals(null);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void Equals_BothTransient_ReturnsFalse()
    {
        // Arrange — two entities with default(Guid) are not equal
        var a = new TestEntity();
        var b = new TestEntity();

        // Act
        var result = a.Equals(b);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void Equals_DifferentRuntimeTypes_ReturnsFalse()
    {
        // Arrange
        var id = Guid.NewGuid();
        var test = new TestEntity();
        var other = new OtherEntity();
        test.SetId(id);
        other.SetId(id);

        // Act
        var result = test.Equals(other);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void OperatorEquals_MatchesEquals()
    {
        // Arrange
        var id = Guid.NewGuid();
        var a = new TestEntity();
        var b = new TestEntity();
        a.SetId(id);
        b.SetId(id);

        // Act & Assert
        (a == b).Should().Be(a.Equals(b));
    }

    [Fact]
    public void OperatorNotEquals_MatchesNotEquals()
    {
        // Arrange
        var a = new TestEntity();
        var b = new TestEntity();
        a.SetId(Guid.NewGuid());
        b.SetId(Guid.NewGuid());

        // Act & Assert
        (a != b).Should().Be(!a.Equals(b));
    }

    [Fact]
    public void GetHashCode_SameId_SameHash()
    {
        // Arrange
        var id = Guid.NewGuid();
        var a = new TestEntity();
        var b = new TestEntity();
        a.SetId(id);
        b.SetId(id);

        // Act & Assert
        a.GetHashCode().Should().Be(b.GetHashCode());
    }
}
