using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Kun.EntityFrameworkCore.Extensions;

/// <summary>Provides Kun-opinionated configuration helpers for <see cref="DbContextOptionsBuilder"/>.</summary>
public static class KunDbContextOptionsBuilderExtensions
{
    /// <summary>
    /// Wires a service-scoped default schema through EF Core's migrations history table so
    /// <c>__ef_migrations_history</c> co-locates with business tables under
    /// <paramref name="schema"/>. Use in tandem with <c>KunDbContext.DefaultSchema</c>
    /// overriding the same value — the <see cref="Kun.EntityFrameworkCore.Interceptors.SchemaConsistencyInterceptor"/>
    /// registered automatically by <see cref="KunDbContext"/> raises a clear exception at the
    /// first <c>SaveChangesAsync</c> if the two diverge.
    /// </summary>
    /// <param name="builder">The options builder to configure. A relational provider
    /// (e.g. <c>.UseNpgsql(...)</c>) must already be applied.</param>
    /// <param name="schema">Service-scoped schema name (e.g. <c>"catalog"</c>).</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="schema"/> is null/empty.</exception>
    /// <exception cref="InvalidOperationException">No relational provider has been configured
    /// on <paramref name="builder"/>.</exception>
    public static DbContextOptionsBuilder UseKunSchema(this DbContextOptionsBuilder builder, string schema)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(schema);

        var relational = builder.Options.Extensions.OfType<RelationalOptionsExtension>().FirstOrDefault()
            ?? throw new InvalidOperationException(
                "UseKunSchema requires a relational provider to be configured first. " +
                "Call .UseNpgsql(...) (or another relational provider) before .UseKunSchema(...).");

        var updated = relational.WithMigrationsHistoryTableSchema(schema);
        ((IDbContextOptionsBuilderInfrastructure)builder).AddOrUpdateExtension(updated);

        return builder;
    }

    /// <summary>Strongly-typed overload of <see cref="UseKunSchema(DbContextOptionsBuilder, string)"/>.</summary>
    public static DbContextOptionsBuilder<TContext> UseKunSchema<TContext>(
        this DbContextOptionsBuilder<TContext> builder, string schema)
        where TContext : DbContext
    {
        UseKunSchema((DbContextOptionsBuilder)builder, schema);
        return builder;
    }
}
