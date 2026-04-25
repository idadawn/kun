namespace Kun.Auditing.DependencyInjection;

/// <summary>
/// Mutable seed bag into which <c>services.AddAuditable&lt;TEntity&gt;()</c> accumulates
/// auditable entity types. At <see cref="Microsoft.Extensions.DependencyInjection.IServiceCollection"/>
/// build time a single <see cref="AuditableEntityRegistry"/> is constructed from this
/// bag and resolved via <see cref="IAuditableEntityRegistry"/>.
/// </summary>
/// <remarks>
/// Kept as a dedicated wrapper (rather than a raw <c>HashSet&lt;Type&gt;</c>) so the DI
/// container hosts a single registered singleton of a named type — matches the Kun.Mediator
/// handler-registration pattern and avoids accidental conflicts with other collections.
/// </remarks>
internal sealed class AuditableEntityTypeCollection
{
    private readonly HashSet<Type> _types = [];

    public void Add(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        _types.Add(type);
    }

    public IReadOnlyCollection<Type> Types => _types;
}
