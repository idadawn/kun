namespace Kun.Auditing;

/// <summary>
/// Documentation-only marker for entity types that should emit
/// <see cref="Events.AuditEventIntegrationEvent"/> rows on every
/// <c>SaveChangesAsync</c>.
/// </summary>
/// <remarks>
/// <para>
/// This attribute is <b>not</b> read at runtime. The audit interceptor consults the
/// DI-populated <see cref="IAuditableEntityRegistry"/> to decide whether a tracked
/// entity participates in auditing. Registration is explicit via
/// <c>services.AddAuditable&lt;TEntity&gt;()</c> — the only runtime source of truth.
/// </para>
/// <para>
/// The attribute exists so that the intent is visible on the type itself (helpful for
/// code review and docs), but adding or removing it has no effect on the running
/// system. This design (plan A3 path c) eliminates reflection-based entity discovery
/// and keeps <c>Kun.Auditing</c> AOT-publish-clean.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class AuditableAttribute : Attribute;
