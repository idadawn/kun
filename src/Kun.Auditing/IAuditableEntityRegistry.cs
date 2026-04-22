namespace Kun.Auditing;

/// <summary>
/// Immutable, DI-populated lookup of entity CLR types that participate in audit logging.
/// Populated at <see cref="Microsoft.Extensions.DependencyInjection.IServiceCollection"/>
/// build time by calls to <c>services.AddAuditable&lt;TEntity&gt;()</c>.
/// </summary>
/// <remarks>
/// The registry is the single runtime source of truth for auditable entities —
/// <see cref="AuditableAttribute"/> is never read at runtime. Backing storage is a
/// <see cref="System.Collections.Frozen.FrozenSet{T}"/> so membership checks inside the
/// EF interceptor are O(1).
/// </remarks>
public interface IAuditableEntityRegistry
{
    /// <summary>
    /// Gets the set of CLR types that were registered via
    /// <c>services.AddAuditable&lt;TEntity&gt;()</c>. Frozen, case-sensitive type identity.
    /// </summary>
    IReadOnlySet<Type> RegisteredTypes { get; }
}
