using FluentAssertions;
using Kun.Core.Domain;
using Kun.Core.MultiTenancy;
using Kun.EntityFrameworkCore;
using Kun.IntegrationTests.Fixtures;
using Kun.MultiTenancy;
using Kun.MultiTenancy.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;
using Xunit;

namespace Kun.IntegrationTests.MultiTenancy;

public sealed class TenantContextBindingTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    [Theory]
    [InlineData("base", false)]
    [InlineData("base", true)]
    [InlineData("context", false)]
    [InlineData("context", true)]
    [InlineData("base-default", false)]
    [InlineData("base-default", true)]
    [InlineData("context-default", false)]
    [InlineData("context-default", true)]
    public async Task Query_SharedModelWithIndependentTenantAccessors_DoesNotUsePreviousAccessor(string mode, bool previousChangedTenant)
    {
        var ct = TestContext.Current.CancellationToken;
        var own = "a-" + Guid.NewGuid().ToString("N");
        var foreign = "b-" + Guid.NewGuid().ToString("N");
        await SeedAsync(own, foreign, ct);
        var firstTenant = new AsyncLocalTenantContext();
        Microsoft.EntityFrameworkCore.Metadata.IModel model;
        using (firstTenant.BeginScope(own))
        {
            await using var first = Context(mode, firstTenant);
            model = first.Model;
            (await first.Set<ContextBindingRow>().SingleAsync(ct)).TenantId.Should().Be(own);
        }

        // A different service provider/request owns a different accessor, but EF reuses the same tenant model.
        using var oldScope = firstTenant.BeginScope(previousChangedTenant ? foreign : null);
        var secondTenant = new AsyncLocalTenantContext();
        using (secondTenant.BeginScope(own))
        {
            await using var second = Context(mode, secondTenant);
            second.Model.Should().BeSameAs(model, "the regression must exercise actual model-cache reuse");
            var rows = await second.Set<ContextBindingRow>().AsNoTracking().ToListAsync(ct);
            rows.Should().ContainSingle().Which.TenantId.Should().Be(own);
        }

        using (secondTenant.BeginScope(foreign))
        {
            await using var other = Context(mode, secondTenant);
            (await other.Set<ContextBindingRow>().SingleAsync(ct)).TenantId.Should().Be(foreign);
        }
    }

    private DbContext Context(string mode, ITenantContext tenant) => mode switch
    {
        "base" => new BaseBindingContext(new DbContextOptionsBuilder<BaseBindingContext>()
            .UseNpgsql(postgres.AppConnectionString).ReplaceService<IModelCacheKeyFactory, KunMultiTenantModelCacheKeyFactory>().Options, tenant),
        "context" => new CurrentBindingContext(new DbContextOptionsBuilder<CurrentBindingContext>()
            .UseNpgsql(postgres.AppConnectionString).ReplaceService<IModelCacheKeyFactory, KunMultiTenantModelCacheKeyFactory>().Options, tenant),
        "base-default" => new BaseBindingContext(new DbContextOptionsBuilder<BaseBindingContext>()
            .UseNpgsql(postgres.AppConnectionString).Options, tenant),
        "context-default" => new CurrentBindingContext(new DbContextOptionsBuilder<CurrentBindingContext>()
            .UseNpgsql(postgres.AppConnectionString).Options, tenant),
        _ => throw new ArgumentOutOfRangeException(nameof(mode))
    };

    private async Task SeedAsync(string own, string foreign, CancellationToken ct)
    {
        // This table deliberately has no RLS: the test must prove the application filter independently.
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand("""
            CREATE TABLE IF NOT EXISTS tenant_context_binding (id uuid PRIMARY KEY, tenant_id text NOT NULL);
            TRUNCATE tenant_context_binding;
            GRANT SELECT ON tenant_context_binding TO kun_app;
            INSERT INTO tenant_context_binding VALUES (@own_id, @own), (@foreign_id, @foreign);
            """, connection);
        command.Parameters.AddWithValue("own_id", Guid.NewGuid());
        command.Parameters.AddWithValue("foreign_id", Guid.NewGuid());
        command.Parameters.AddWithValue("own", own);
        command.Parameters.AddWithValue("foreign", foreign);
        await command.ExecuteNonQueryAsync(ct);
    }
}

internal sealed class ContextBindingRow : IMultiTenant
{
    public Guid Id { get; set; }
    public string? TenantId { get; set; }
}

internal sealed class BaseBindingContext(DbContextOptions<BaseBindingContext> options, ITenantContext tenant) : KunDbContext(options, tenant)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        Configure(modelBuilder);
        base.OnModelCreating(modelBuilder);
    }

    internal static void Configure(ModelBuilder modelBuilder) => modelBuilder.Entity<ContextBindingRow>(entity =>
    {
        entity.ToTable("tenant_context_binding");
        entity.HasKey(row => row.Id);
        entity.Property(row => row.Id).HasColumnName("id");
        entity.Property(row => row.TenantId).HasColumnName("tenant_id");
    });
}

internal sealed class CurrentBindingContext(DbContextOptions<CurrentBindingContext> options, ITenantContext tenant) : DbContext(options), IKunMultiTenantDbContext
{
    public string? TenantIdForModelCacheKey => tenant.TenantId;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        BaseBindingContext.Configure(modelBuilder);
        modelBuilder.ApplyKunMultiTenantFilters(this);
    }
}
