namespace Kun.Audit.Abstractions;

/// <summary>
/// Declarative marker for Command / Query / endpoint types that should be
/// audited regardless of whether their execution touched any tracked
/// aggregate. Interceptor-driven audits handle entity CRUD automatically;
/// this attribute covers the other category — business actions whose
/// significance isn't visible in the change-tracker (e.g. login,
/// download, export, permission grant).
/// </summary>
/// <remarks>
/// <para>
/// The attribute is consumed by consumer-side Mediator pipeline behaviors
/// / endpoint filters. Kun.Audit itself does not ship an interceptor for
/// this attribute so that applications stay free to choose their
/// dispatch framework. A sample wiring lives in the framework docs.
/// </para>
/// <para>
/// Does not alter serialization or persistence — only metadata.
/// </para>
/// </remarks>
/// <param name="action">Stable audit action (e.g. <c>agent.exported</c>).</param>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = false)]
public sealed class AuditableAttribute(string action) : Attribute
{
    /// <summary>Stable verb identifying what happened — persisted verbatim to <see cref="AuditLog.Action"/>.</summary>
    public string Action { get; } = action;

    /// <summary>Optional resource-type override. When unset, the consumer's pipeline behavior will derive it from the annotated type's name.</summary>
    public string? ResourceType { get; init; }
}
