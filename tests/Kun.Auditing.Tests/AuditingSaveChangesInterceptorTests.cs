using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Kun.Auditing.Tests;

// Why Npgsql with an unreachable host?
// ------------------------------------
// The interceptor runs inside EF Core's SavingChangesAsync pipeline, which is triggered
// from DbContext.SaveChangesAsync. We need a real DbContext + real ChangeTracker entries
// to exercise the production code path, but we do NOT want to stand up Docker for a unit
// test. EF Core fires SavingChangesAsync BEFORE opening a connection; pointing at an
// unreachable host lets the interceptor run to completion, then the outer call fails at
// the network layer — which we swallow. The side-effect on the fake sink is the assertion
// target. This mirrors how SchemaConsistencyInterceptorTests handle the same problem.

public sealed class AuditingSaveChangesInterceptorTests
{
    private const string UnreachableConnectionString =
        "Host=kun-auditing-noop.invalid;Database=kun_test;Username=kun;Password=kun;Timeout=2;Command Timeout=2";

    [Fact]
    public async Task Unregistered_entity_is_ignored_by_the_interceptor()
    {
        var sink = new CapturingSink();
        var interceptor = BuildInterceptor(sink, registered: []);

        await using var ctx = BuildContext(interceptor);
        ctx.UnregisteredItems.Add(new UnregisteredItem { Id = Guid.NewGuid(), Name = "x" });

        await SaveIgnoringNetworkFailure(ctx);

        sink.Events.Should().BeEmpty("path (c) means no runtime attribute scan — AddAuditable<T> is the sole source of truth");
    }

    [Fact]
    public async Task Registered_entity_emits_AuditEventIntegrationEvent_per_change()
    {
        var sink = new CapturingSink();
        var auditCtx = new AuditContext
        {
            IpAddress = "10.0.0.1",
            UserAgent = "test-agent/1.0",
            TraceId = "0af7651916cd43dd8448eb211c80319c",
            ActorId = "user-42",
            ActorType = "user",
        };

        var interceptor = BuildInterceptor(sink, registered: [typeof(AuditedOrder)], auditContext: auditCtx);

        await using var ctx = BuildContext(interceptor);
        var orderId = Guid.NewGuid();
        ctx.Orders.Add(new AuditedOrder { Id = orderId, Name = "order-1" });

        await SaveIgnoringNetworkFailure(ctx);

        sink.Events.Should().HaveCount(1);
        var evt = sink.Events[0];

        // Full 13-field payload per M4.
        evt.TenantId.Should().BeNull();
        evt.UserId.Should().BeNull();
        evt.ActorType.Should().Be("user");
        evt.ActorId.Should().Be("user-42");
        evt.Action.Should().Be("Added");
        evt.ResourceType.Should().Be(nameof(AuditedOrder));
        evt.ResourceId.Should().Be(orderId.ToString());
        evt.Outcome.Should().Be("success");
        evt.TraceId.Should().Be("0af7651916cd43dd8448eb211c80319c");
        evt.Metadata.Should().BeNull();
        evt.IpAddress.Should().Be("10.0.0.1");
        evt.UserAgent.Should().Be("test-agent/1.0");
        evt.CreatedAt.Should().NotBe(default);
    }

    [Fact]
    public async Task Null_AuditContext_fields_surface_as_null_on_the_event()
    {
        var sink = new CapturingSink();
        var auditCtx = new AuditContext(); // defaults: ActorType="system", everything else null
        var interceptor = BuildInterceptor(sink, registered: [typeof(AuditedOrder)], auditContext: auditCtx);

        await using var ctx = BuildContext(interceptor);
        ctx.Orders.Add(new AuditedOrder { Id = Guid.NewGuid(), Name = "order-2" });

        await SaveIgnoringNetworkFailure(ctx);

        sink.Events.Should().HaveCount(1);
        var evt = sink.Events[0];

        evt.IpAddress.Should().BeNull();
        evt.UserAgent.Should().BeNull();
        evt.ActorId.Should().BeNull();
        evt.ActorType.Should().Be("system", "default actor type for uninitialised contexts");
    }

    [Fact]
    public async Task Mixed_change_set_only_emits_for_registered_types()
    {
        var sink = new CapturingSink();
        var interceptor = BuildInterceptor(sink, registered: [typeof(AuditedOrder)]);

        await using var ctx = BuildContext(interceptor);
        ctx.Orders.Add(new AuditedOrder { Id = Guid.NewGuid(), Name = "tracked" });
        ctx.UnregisteredItems.Add(new UnregisteredItem { Id = Guid.NewGuid(), Name = "ignored" });

        await SaveIgnoringNetworkFailure(ctx);

        sink.Events.Should().HaveCount(1);
        sink.Events[0].ResourceType.Should().Be(nameof(AuditedOrder));
    }

    // -------------------------------------------------------------------------
    // Fixtures
    // -------------------------------------------------------------------------

    private static AuditingSaveChangesInterceptor BuildInterceptor(
        IAuditSink sink,
        IEnumerable<Type> registered,
        AuditContext? auditContext = null)
    {
        var registry = new AuditableEntityRegistry(registered);
        return new AuditingSaveChangesInterceptor(
            registry,
            sink,
            auditContext ?? new AuditContext(),
            TimeProvider.System,
            NullLogger<AuditingSaveChangesInterceptor>.Instance);
    }

    private static AuditTestDbContext BuildContext(AuditingSaveChangesInterceptor interceptor)
    {
        var options = new DbContextOptionsBuilder<AuditTestDbContext>()
            .UseNpgsql(UnreachableConnectionString)
            .ReplaceService<IModelCacheKeyFactory, UncachedModelCacheKeyFactory>()
            .AddInterceptors(interceptor)
            .Options;
        return new AuditTestDbContext(options);
    }

    // The network call is expected to fail. All assertions are on the captured sink,
    // which has already been populated by the interceptor BEFORE EF Core opens the
    // connection.
    private static async Task SaveIgnoringNetworkFailure(DbContext ctx)
    {
        try
        {
            await ctx.SaveChangesAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Connection errors, Npgsql-specific exceptions, etc. are the expected path:
            // the interceptor has already run by now.
        }
    }
}

internal sealed class CapturingSink : IAuditSink
{
    public List<Events.AuditEventIntegrationEvent> Events { get; } = [];

    public ValueTask WriteAsync(Events.AuditEventIntegrationEvent @event, CancellationToken cancellationToken)
    {
        Events.Add(@event);
        return ValueTask.CompletedTask;
    }
}

internal sealed class UncachedModelCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime) => new object();
}

internal sealed class AuditedOrder
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

internal sealed class UnregisteredItem
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

internal sealed class AuditTestDbContext : DbContext
{
    public DbSet<AuditedOrder> Orders => Set<AuditedOrder>();
    public DbSet<UnregisteredItem> UnregisteredItems => Set<UnregisteredItem>();

    public AuditTestDbContext(DbContextOptions<AuditTestDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<AuditedOrder>(b =>
        {
            b.ToTable("audited_orders");
            b.HasKey(o => o.Id);
            b.Property(o => o.Name).HasMaxLength(200);
        });
        modelBuilder.Entity<UnregisteredItem>(b =>
        {
            b.ToTable("unregistered_items");
            b.HasKey(o => o.Id);
            b.Property(o => o.Name).HasMaxLength(200);
        });
    }
}
