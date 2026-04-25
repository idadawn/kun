using System.Collections.Frozen;

namespace Kun.Auditing;

/// <summary>
/// Default <see cref="IAuditableEntityRegistry"/> backed by a <see cref="FrozenSet{T}"/>
/// of CLR types. Constructed from the seed collection populated by
/// <c>services.AddAuditable&lt;TEntity&gt;()</c>.
/// </summary>
internal sealed class AuditableEntityRegistry : IAuditableEntityRegistry
{
    /// <summary>Creates a registry backed by <paramref name="types"/>.</summary>
    /// <param name="types">Seed sequence. Duplicates are collapsed by type identity.</param>
    public AuditableEntityRegistry(IEnumerable<Type> types)
    {
        ArgumentNullException.ThrowIfNull(types);
        RegisteredTypes = types.ToFrozenSet();
    }

    /// <inheritdoc />
    public IReadOnlySet<Type> RegisteredTypes { get; }
}
