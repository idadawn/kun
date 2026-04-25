using FluentAssertions;
using Kun.Auditing.DependencyInjection;
using Kun.Core.Domain;
using Kun.Core.MultiTenancy;
using Kun.EntityFrameworkCore;
using Kun.EntityFrameworkCore.DependencyInjection;
using Kun.EventBus.DependencyInjection;
using Kun.Mediator.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Xunit;

namespace Kun.IntegrationTests.Auditing;

// ---------------------------------------------------------------------------
// Minimal test types — internal, non-file-scoped class declarations so EF Core
// 10's ShortName() substring-on-'<' crash on compiler-mangled file-scoped type
// names does not fire. See OutboxAtomicityTests for the canonical explanation.
// ---------------------------------------------------------------------------

internal sealed class AuditOrder : AggregateRoot<Guid>
{
    public string Name { get; private set; } = string.Empty;

    public static AuditOrder Create(Guid id, string name) =>
        new() { Id = id, Name = name };
}

internal sealed class AuditFlowDbContext : KunDbContext
{
    public DbSet<AuditOrder> Orders => Set<AuditOrder>();

    public AuditFlowDbContext(DbContextOptions<AuditFlowDbContext> options, ITenantContext tenantContext)
        : base(options, tenantContext) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<AuditOrder>(b =>
        {
            b.ToTable("audit_orders");
            b.HasKey(o => o.Id);
            b.Property(o => o.Name).HasMaxLength(200);
            b.Property(o => o.CreatedAt);
            b.Property(o => o.CreatedBy);
            b.Property(o => o.UpdatedAt);
            b.Property(o => o.UpdatedBy);
            b.Property(o => o.IsDeleted);
            b.Property(o => o.DeletedAt);
        });
    }
}

public sealed class AuditEventFlowTests : IAsyncLifetime
{
    private PostgreSqlContainer _postgres = null!;
    private IServiceProvider _services = null!;

    public async ValueTask InitializeAsync()
    {
        try
        {
            _postgres = new PostgreSqlBuilder()
                .WithImage("postgres:16")
                .WithDatabase("kun_test")
                .WithUsername("kun")
                .WithPassword("kun_secret")
                .Build();
        }
        catch (ArgumentException ex) when (ex.Message.Contains("Docker", StringComparison.OrdinalIgnoreCase))
        {
            Assert.Skip("Docker is not available on this host — integration test requires Docker (runs in CI).");
            return;
        }

        await _postgres.StartAsync(TestContext.Current.CancellationToken);

        var services = new ServiceCollection();

        services.AddSingleton<ITenantContext>(NullTenantContext.Instance);
        services.AddKunMediator(_ => { });
        services.AddKunEventBus(b => b.RegisterAuditing());
        services.AddKunEntityFrameworkCore();
        services.AddKunAuditing();
        services.AddAuditable<AuditOrder>();

        services.AddDbContext<AuditFlowDbContext>((sp, opts) =>
            opts.UseNpgsql(_postgres.GetConnectionString())
                .AddAuditingInterceptor(sp));

        // EfUnitOfWork depends on KunDbContext — alias the concrete subclass.
        services.AddScoped<KunDbContext>(sp => sp.GetRequiredService<AuditFlowDbContext>());

        _services = services.BuildServiceProvider();

        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AuditFlowDbContext>();
        await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_services is IAsyncDisposable ad) await ad.DisposeAsync();
        if (_postgres is not null) await _postgres.DisposeAsync();
    }

    /// <summary>
    /// P1-2 end-to-end invariant: mutating a registered entity produces one
    /// <c>kun.audit.event.v1</c> Outbox row per affected change, committed atomically
    /// with the business write.
    /// </summary>
    [Fact]
    public async Task Mutating_registered_entity_emits_kun_audit_event_v1_outbox_row()
    {
        var ct = TestContext.Current.CancellationToken;

        await using var scope = _services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var db = sp.GetRequiredService<AuditFlowDbContext>();
        var uow = sp.GetRequiredService<IUnitOfWork>();

        var order = AuditOrder.Create(Guid.NewGuid(), "widget");
        db.Orders.Add(order);

        await uow.SaveChangesAsync(ct);

        var auditRows = await db.OutboxMessages
            .Where(m => m.EventTypeId == AuditingServiceCollectionExtensions.AuditEventStableId)
            .ToListAsync(ct);

        auditRows.Should().HaveCount(1, "one audit event per tracked change of a registered entity");
        auditRows[0].EventTypeId.Should().Be("kun.audit.event.v1");
        auditRows[0].Payload.Should().Contain("AuditOrder", "ResourceType should be the CLR type name");
        auditRows[0].Payload.Should().Contain("Added", "Action should reflect the EntityState");
        auditRows[0].Payload.Should().Contain(order.Id.ToString(), "ResourceId should carry the aggregate primary key");
    }
}
