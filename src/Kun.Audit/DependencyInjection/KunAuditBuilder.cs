using Microsoft.Extensions.DependencyInjection;

namespace Kun.Audit.DependencyInjection;

/// <summary>
/// Fluent builder returned by <c>AddKunAudit</c>. Extension methods on
/// this type register concrete <c>IAuditWriter</c> backends without
/// forcing consumers to call multiple top-level <c>Add*</c> methods.
/// </summary>
public sealed class KunAuditBuilder
{
    /// <summary>Creates a builder bound to <paramref name="services"/>.</summary>
    public KunAuditBuilder(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Services = services;
    }

    /// <summary>The service collection being configured.</summary>
    public IServiceCollection Services { get; }
}
