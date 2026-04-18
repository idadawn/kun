namespace Kun.Core.Domain;

/// <summary>
/// DDD 实体基类。实体的相等性由 <see cref="Id"/> 决定，而非属性集合（这是实体与值对象的核心区别）。
/// </summary>
/// <typeparam name="TKey">主键类型，通常为 <see cref="Guid"/>、<see langword="long"/> 或 <see langword="string"/>。</typeparam>
public abstract class Entity<TKey> : IEquatable<Entity<TKey>>
    where TKey : notnull
{
    /// <summary>Entity primary key.</summary>
    public TKey Id { get; protected set; } = default!;

    /// <summary>Returns <see langword="true"/> if this entity has the same type and id as <paramref name="other"/>.</summary>
    public bool Equals(Entity<TKey>? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (GetType() != other.GetType()) return false;

        // 默认值（未持久化的瞬时实体）不等同
        if (EqualityComparer<TKey>.Default.Equals(Id, default!)) return false;
        if (EqualityComparer<TKey>.Default.Equals(other.Id, default!)) return false;

        return EqualityComparer<TKey>.Default.Equals(Id, other.Id);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Entity<TKey> e && Equals(e);

    /// <summary>Returns a hash code based on <see cref="Id"/>.</summary>
    public override int GetHashCode() => Id?.GetHashCode() ?? 0;

    /// <summary>Equality operator delegating to <see cref="Equals(Entity{TKey}?)"/>.</summary>
    public static bool operator ==(Entity<TKey>? left, Entity<TKey>? right)
        => left is null ? right is null : left.Equals(right);

    /// <summary>Inequality operator delegating to <see cref="operator ==(Entity{TKey}?, Entity{TKey}?)"/>.</summary>
    public static bool operator !=(Entity<TKey>? left, Entity<TKey>? right)
        => !(left == right);
}
