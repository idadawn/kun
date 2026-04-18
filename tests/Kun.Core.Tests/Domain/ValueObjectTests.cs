using FluentAssertions;
using Kun.Core.Domain;
using Xunit;

namespace Kun.Core.Tests.Domain;

file sealed class Money : ValueObject
{
    public decimal Amount { get; }
    public string Currency { get; }

    public Money(decimal amount, string currency)
    {
        ArgumentNullException.ThrowIfNull(currency);
        Amount = amount;
        Currency = currency;
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Amount;
        yield return Currency;
    }
}

file sealed class Weight : ValueObject
{
    public decimal Value { get; }
    public string Unit { get; }

    public Weight(decimal value, string unit)
    {
        Value = value;
        Unit = unit;
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
        yield return Unit;
    }
}

public class ValueObjectTests
{
    [Fact]
    public void Equals_SameComponents_ReturnsTrue()
    {
        // Arrange
        var a = new Money(10m, "USD");
        var b = new Money(10m, "USD");

        // Act
        var result = a.Equals(b);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void Equals_DifferentAmount_ReturnsFalse()
    {
        // Arrange
        var a = new Money(10m, "USD");
        var b = new Money(20m, "USD");

        // Act
        var result = a.Equals(b);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void Equals_DifferentCurrency_ReturnsFalse()
    {
        // Arrange
        var a = new Money(10m, "USD");
        var b = new Money(10m, "EUR");

        // Act
        var result = a.Equals(b);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void Equals_NullOther_ReturnsFalse()
    {
        // Arrange
        var money = new Money(10m, "USD");

        // Act
        var result = money.Equals(null);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void Equals_DifferentRuntimeType_ReturnsFalse()
    {
        // Arrange — same component values, different types
        var money = new Money(10m, "USD");
        var weight = new Weight(10m, "USD");

        // Act
        var result = money.Equals(weight);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void GetHashCode_SameComponents_SameHash()
    {
        // Arrange
        var a = new Money(10m, "USD");
        var b = new Money(10m, "USD");

        // Act & Assert
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void GetHashCode_DifferentComponents_DifferentHash()
    {
        // Arrange
        var a = new Money(10m, "USD");
        var b = new Money(20m, "EUR");

        // Act & Assert
        a.GetHashCode().Should().NotBe(b.GetHashCode());
    }

    [Fact]
    public void OperatorEquals_MatchesEquals()
    {
        // Arrange
        var a = new Money(10m, "USD");
        var b = new Money(10m, "USD");

        // Act & Assert
        (a == b).Should().Be(a.Equals(b));
    }

    [Fact]
    public void OperatorNotEquals_MatchesNotEquals()
    {
        // Arrange
        var a = new Money(10m, "USD");
        var b = new Money(20m, "EUR");

        // Act & Assert
        (a != b).Should().Be(!a.Equals(b));
    }
}
