using Kun.Core.MultiTenancy;
using Kun.EntityFrameworkCore.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kun.EntityFrameworkCore.MultiTenancy;

/// <summary>
/// DI entry point for Kun's SaaS multi-tenancy strategy selection. Dispatches based on
/// <see cref="KunMultiTenancyOptions.Strategy"/>.
/// </summary>
public static class KunMultiTenancyServiceCollectionExtensions
{
    /// <summary>
    /// Registers the multi-tenancy services for a specific DbContext type. Must be called
    /// BEFORE the matching <c>AddDbContext&lt;TContext&gt;</c>, and the consumer is
    /// responsible for registering their own <see cref="ITenantDbContextFactory{TContext}"/>
    /// when <see cref="KunMultiTenancyOptions.Strategy"/> is
    /// <see cref="TenantIsolationStrategy.SchemaPerTenant"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Shared</strong>: delegates to <c>AddKunTenantRls()</c> — no additional services
    /// beyond the RLS interceptor and <see cref="NullTenantProvisioner"/> default.
    /// </para>
    /// <para>
    /// <strong>SchemaPerTenant</strong>: registers <see cref="TenantAwareModelCacheKeyFactory"/>,
    /// the <see cref="Kun.EntityFrameworkCore.Interceptors.TenantRlsTransactionInterceptor"/> (defence-in-depth), a
    /// Singleton <see cref="SchemaPerTenantProvisioner{TContext}"/> as the
    /// <see cref="ITenantProvisioner"/> implementation, and binds
    /// <see cref="KunMultiTenancyOptions"/>. Consumers must also add the schema selection
    /// and the model-cache factory to their DbContext options, e.g.:
    /// <code>
    /// services.AddDbContext&lt;CatalogDbContext&gt;((sp, opts) =&gt;
    ///     opts.UseNpgsql(cs)
    ///         .UseKunSchema(sp.GetRequiredService&lt;ITenantContext&gt;().TenantId ?? "public")
    ///         .ReplaceService&lt;IModelCacheKeyFactory, TenantAwareModelCacheKeyFactory&gt;()
    ///         .AddInterceptors(sp.GetRequiredService&lt;TenantRlsTransactionInterceptor&gt;()));
    /// </code>
    /// </para>
    /// <para>
    /// <strong>DatabasePerTenant</strong>: not implemented in Part 2; resolution throws
    /// <see cref="NotImplementedException"/> pointing at the P2-D Part 3 track.
    /// </para>
    /// <para>
    /// <strong>Strategy snapshotting</strong>: the <see cref="KunMultiTenancyOptions.Strategy"/>
    /// value set by <paramref name="configure"/> is captured at registration time to decide
    /// which services to register. A subsequent
    /// <c>services.Configure&lt;KunMultiTenancyOptions&gt;</c> that mutates
    /// <see cref="KunMultiTenancyOptions.Strategy"/> will NOT retro-actively change the DI
    /// wiring — the options-read path would disagree with the registered graph. Set the
    /// strategy exactly once, inside this call's <paramref name="configure"/> delegate.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddKunMultiTenancy<TContext>(
        this IServiceCollection services,
        Action<KunMultiTenancyOptions> configure)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.Configure(configure);

        var tempOptions = new KunMultiTenancyOptions();
        configure(tempOptions);

        switch (tempOptions.Strategy)
        {
            case TenantIsolationStrategy.Shared:
                services.AddKunTenantRls();
                services.TryAddSingleton<ITenantProvisioner>(NullTenantProvisioner.Instance);
                break;

            case TenantIsolationStrategy.SchemaPerTenant:
                services.AddKunTenantRls();
                services.TryAddSingleton<TenantAwareModelCacheKeyFactory>();
                services.TryAddSingleton<ITenantProvisioner, SchemaPerTenantProvisioner<TContext>>();
                break;

            case TenantIsolationStrategy.DatabasePerTenant:
                // TryAddSingleton: consumers who pre-register their own ITenantProvisioner win.
                // Consistent with the Shared / SchemaPerTenant branches above.
                services.TryAddSingleton<ITenantProvisioner>(_ =>
                    throw new NotImplementedException(
                        "DatabasePerTenant strategy is deferred to P2-D Part 3. See " +
                        "docs/architecture/adr-p2d-multitenancy.md for status."));
                break;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(configure),
                    tempOptions.Strategy,
                    $"Unsupported TenantIsolationStrategy '{tempOptions.Strategy}'.");
        }

        return services;
    }
}
