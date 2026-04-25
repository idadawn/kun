using System.Linq.Expressions;
using Kun.Core.Domain;
using Kun.Core.MultiTenancy;
using Kun.EntityFrameworkCore.Interceptors;
using Kun.EventBus.Outbox;
using Microsoft.EntityFrameworkCore;

namespace Kun.EntityFrameworkCore;

/// <summary>Base EF Core DbContext for Kun Framework services.</summary>
/// <remarks>
/// Applies automatic global query filters for <see cref="IMultiTenant"/> entities
/// (filters out rows whose <c>TenantId</c> does not match the current tenant scope)
/// and for <see cref="ISoftDelete"/> entities (excludes soft-deleted rows).
/// Ensures the Outbox is registered for atomic enqueue.
/// </remarks>
public abstract class KunDbContext : DbContext
{
    private readonly ITenantContext _tenantContext;

    /// <summary>The Outbox message store.</summary>
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    /// <summary>Creates a new <see cref="KunDbContext"/>.</summary>
    protected KunDbContext(DbContextOptions options, ITenantContext tenantContext) : base(options)
    {
        ArgumentNullException.ThrowIfNull(tenantContext);
        _tenantContext = tenantContext;
    }

    /// <summary>
    /// Default database schema for this context. When non-<see langword="null"/>, applied via
    /// <see cref="RelationalModelBuilderExtensions.HasDefaultSchema(ModelBuilder, string?)"/>
    /// before entity configurations run — tables, Outbox, and Saga storage all land under this
    /// schema by default. Callers should also configure <c>MigrationsHistoryTable</c> to the
    /// same schema on the Npgsql options so migrations + business tables co-locate.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Overriding services' DbContexts (e.g. <c>CatalogDbContext</c>) should return a service-scoped
    /// schema like <c>"catalog"</c> to enable single-database, multi-schema isolation
    /// (SAD §5.1). Leaving this <see langword="null"/> preserves legacy <c>public</c>-schema
    /// behavior.
    /// </para>
    /// </remarks>
    protected virtual string? DefaultSchema { get; }

    /// <inheritdoc/>
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);

        // Always register SchemaConsistencyInterceptor so that services which override
        // DefaultSchema but forget UseKunSchema(schema) fail fast at the first
        // SaveChangesAsync with a clear remediation message, rather than silently
        // writing business tables under the service schema while __ef_migrations_history
        // stays in "public" (the M5 M5 mis-wired-migrations hazard).
        optionsBuilder.AddInterceptors(new SchemaConsistencyInterceptor());
    }

    /// <inheritdoc/>
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);

        // AggregateRoot exposes DomainEvents as IReadOnlyCollection<IDomainEvent>. EF Core's
        // model builder treats that as a navigation collection to IDomainEvent and crashes at
        // query-pipeline navigation expansion (IDomainEvent has no storage shape). Tell the
        // convention layer to ignore this type everywhere so the property is skipped on every
        // aggregate, including derived types discovered after OnModelCreating runs.
        configurationBuilder.IgnoreAny<IDomainEvent>();
    }

    /// <inheritdoc/>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        if (DefaultSchema is not null)
        {
            modelBuilder.HasDefaultSchema(DefaultSchema);
        }
        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
        ApplyGlobalQueryFilters(modelBuilder);
    }

    /// <summary>
    /// Exposes the current tenant ID for use by a custom <c>IModelCacheKeyFactory</c>
    /// so EF Core builds a distinct compiled model per tenant rather than sharing the
    /// first-built model across all instances of the same DbContext type.
    /// </summary>
    public string? TenantIdForModelCacheKey => _tenantContext.TenantId;

    private void ApplyGlobalQueryFilters(ModelBuilder modelBuilder)
    {
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            var clrType = entity.ClrType;
            var isMultiTenant = typeof(IMultiTenant).IsAssignableFrom(clrType);
            var isSoftDelete = typeof(ISoftDelete).IsAssignableFrom(clrType);

            if (!isMultiTenant && !isSoftDelete)
            {
                continue;
            }

            var parameter = Expression.Parameter(clrType, "e");
            Expression? combined = null;

            if (isMultiTenant)
            {
                var tenantIdProp = Expression.Property(parameter, nameof(IMultiTenant.TenantId));
                var currentTenantId = Expression.Property(
                    Expression.Constant(_tenantContext),
                    nameof(ITenantContext.TenantId));

                // e.TenantId == _tenantContext.TenantId
                combined = Expression.Equal(tenantIdProp, currentTenantId);
            }

            if (isSoftDelete)
            {
                var isDeletedProp = Expression.Property(parameter, nameof(ISoftDelete.IsDeleted));

                // !e.IsDeleted
                var notDeleted = Expression.Not(isDeletedProp);
                combined = combined is null ? notDeleted : Expression.AndAlso(combined, notDeleted);
            }

            // HasQueryFilter can only be called once per entity type — calling it a second
            // time overwrites the previous filter. By merging both predicates here we ensure
            // entities implementing both IMultiTenant and ISoftDelete (e.g. AggregateRoot
            // subclasses) get a single combined filter: TenantId == X && !IsDeleted.
            modelBuilder.Entity(clrType).HasQueryFilter(
                Expression.Lambda(combined!, parameter));
        }
    }
}
