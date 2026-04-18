namespace Kun.Core.Domain;

/// <summary>
/// DDD 值对象基类。值对象没有独立标识，相等性由其所有组成属性决定。
/// 值对象应当是不可变的——修改应返回新实例，而非改变现有实例。
/// </summary>
public abstract class ValueObject : IEquatable<ValueObject>
{
    /// <summary>
    /// 返回参与相等性比较的所有组成部分。子类必须覆盖此方法，yield return 每个组成字段。
    /// </summary>
    protected abstract IEnumerable<object?> GetEqualityComponents();

    /// <summary>Returns <see langword="true"/> when all equality components of both value objects are equal.</summary>
    public bool Equals(ValueObject? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (GetType() != other.GetType()) return false;

        return GetEqualityComponents().SequenceEqual(other.GetEqualityComponents());
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is ValueObject vo && Equals(vo);

    /// <summary>Returns a hash code derived from all equality components.</summary>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var component in GetEqualityComponents())
        {
            hash.Add(component);
        }
        return hash.ToHashCode();
    }

    /// <summary>Equality operator delegating to <see cref="Equals(ValueObject?)"/>.</summary>
    public static bool operator ==(ValueObject? left, ValueObject? right)
        => left is null ? right is null : left.Equals(right);

    /// <summary>Inequality operator delegating to <see cref="operator ==(ValueObject?, ValueObject?)"/>.</summary>
    public static bool operator !=(ValueObject? left, ValueObject? right) => !(left == right);
}
