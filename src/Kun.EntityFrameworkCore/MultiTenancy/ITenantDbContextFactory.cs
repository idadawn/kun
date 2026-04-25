using Microsoft.EntityFrameworkCore;

namespace Kun.EntityFrameworkCore.MultiTenancy;

/// <summary>
/// Builds a <typeparamref name="TContext"/> instance bound to a specific tenant's schema
/// or database. Used by <see cref="SchemaPerTenantProvisioner{TContext}"/> at tenant
/// provisioning time, when the ambient <see cref="Kun.Core.MultiTenancy.ITenantContext"/>
/// may not yet reference the new tenant.
/// </summary>
/// <remarks>
/// <para>
/// Consumers implement this interface once per DbContext type. A minimal implementation
/// wraps <c>DbContextOptionsBuilder.UseNpgsql(...).UseKunSchema(tenantId)</c> and returns
/// a freshly-constructed <typeparamref name="TContext"/>.
/// </para>
/// <para>
/// For <strong>runtime query access</strong> (inside a request), prefer the standard
/// DI-injected <typeparamref name="TContext"/> registered via
/// <c>AddDbContext&lt;TContext&gt;((sp, opts) =&gt; opts.UseKunSchema(sp.GetRequiredService&lt;ITenantContext&gt;().TenantId!))</c>
/// combined with <see cref="TenantAwareModelCacheKeyFactory"/>. The factory is the explicit
/// escape hatch for administrative / provisioning flows that must target a specific tenant
/// regardless of the ambient context.
/// </para>
/// </remarks>
/// <typeparam name="TContext">The concrete <see cref="DbContext"/> subclass.</typeparam>
public interface ITenantDbContextFactory<TContext> where TContext : DbContext
{
    /// <summary>Builds a <typeparamref name="TContext"/> bound to the specified tenant's schema.</summary>
    /// <param name="tenantId">Non-empty tenant ID.</param>
    /// <returns>A new, caller-owned <typeparamref name="TContext"/>; caller must dispose.</returns>
    TContext CreateForTenant(string tenantId);
}
