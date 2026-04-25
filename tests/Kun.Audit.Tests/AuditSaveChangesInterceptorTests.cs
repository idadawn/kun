using FluentAssertions;
using Kun.Audit.Abstractions;
using Kun.Audit.DependencyInjection;
using Kun.Audit.EntityFrameworkCore;
using Kun.Core.Domain;
using Kun.Core.MultiTenancy;
using Kun.Core.Security;
using Kun.Core.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Kun.Audit.Tests;

public sealed class AuditSaveChangesInterceptorTests
{
    [Fact]
    public async Task Added_Aggregate_EmitsCreatedAudit()
    {
        var fixture = new InterceptorFixture(tenantId: "tenant-a", userId: "user-1");
        var widget = new Widget { Id = Guid.NewGuid(), TenantId = "tenant-a", Name = "hello" };

        fixture.DbContext.Add(widget);
        await fixture.DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        fixture.Entries.Should().HaveCount(1);
        var audit = fixture.Entries[0];
        audit.Action.Should().Be("widget.created");
        audit.ResourceType.Should().Be("Widget");
        audit.ResourceId.Should().Be(widget.Id.ToString());
        audit.ActorId.Should().Be("user-1");
        audit.ActorType.Should().Be(AuditActorType.User);
        audit.TenantId.Should().Be("tenant-a");
    }

    [Fact]
    public async Task Modified_Aggregate_EmitsUpdatedAudit()
    {
        var fixture = new InterceptorFixture(tenantId: "tenant-a", userId: "user-1");
        var widget = new Widget { Id = Guid.NewGuid(), TenantId = "tenant-a", Name = "initial" };
        fixture.DbContext.Add(widget);
        await fixture.DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        fixture.Entries.Clear();

        widget.Name = "updated";
        await fixture.DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        fixture.Entries.Should().HaveCount(1);
        fixture.Entries[0].Action.Should().Be("widget.updated");
    }

    [Fact]
    public async Task Deleted_Aggregate_EmitsDeletedAudit()
    {
        var fixture = new InterceptorFixture(tenantId: "tenant-a", userId: "user-1");
        var widget = new Widget { Id = Guid.NewGuid(), TenantId = "tenant-a", Name = "byebye" };
        fixture.DbContext.Add(widget);
        await fixture.DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        fixture.Entries.Clear();

        fixture.DbContext.Remove(widget);
        await fixture.DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        fixture.Entries.Should().HaveCount(1);
        fixture.Entries[0].Action.Should().Be("widget.deleted");
    }

    [Fact]
    public async Task AnonymousUser_EmitsSystemActorType()
    {
        var fixture = new InterceptorFixture(tenantId: "tenant-a", userId: null);
        var widget = new Widget { Id = Guid.NewGuid(), TenantId = "tenant-a", Name = "bg" };

        fixture.DbContext.Add(widget);
        await fixture.DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        fixture.Entries[0].ActorType.Should().Be(AuditActorType.System);
        fixture.Entries[0].ActorId.Should().BeNull();
    }

    [Fact]
    public async Task WriterException_IsSwallowedByDefault()
    {
        var fixture = new InterceptorFixture(tenantId: "tenant-a", userId: "user-1");
        fixture.Writer
            .When(w => w.WriteAsync(Arg.Any<AuditLog>(), Arg.Any<CancellationToken>()))
            .Do(_ => throw new InvalidOperationException("boom"));
        var widget = new Widget { Id = Guid.NewGuid(), TenantId = "tenant-a", Name = "ignored" };

        fixture.DbContext.Add(widget);
        var act = async () => await fixture.DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task WriterException_Surfaces_WhenSwallowingDisabled()
    {
        var fixture = new InterceptorFixture(
            tenantId: "tenant-a",
            userId: "user-1",
            configure: o => o.SwallowWriterExceptions = false);
        fixture.Writer
            .When(w => w.WriteAsync(Arg.Any<AuditLog>(), Arg.Any<CancellationToken>()))
            .Do(_ => throw new InvalidOperationException("boom"));
        var widget = new Widget { Id = Guid.NewGuid(), TenantId = "tenant-a", Name = "strict" };

        fixture.DbContext.Add(widget);
        var act = async () => await fixture.DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");
    }

    private sealed class InterceptorFixture
    {
        public List<AuditLog> Entries { get; } = [];
        public IAuditWriter Writer { get; }
        public TestDbContext DbContext { get; }

        public InterceptorFixture(
            string tenantId,
            string? userId,
            Action<KunAuditOptions>? configure = null)
        {
            var tenant = Substitute.For<ITenantContext>();
            tenant.TenantId.Returns(tenantId);

            var user = Substitute.For<ICurrentUser>();
            user.UserId.Returns(userId);
            user.IsAuthenticated.Returns(userId is not null);

            var options = new KunAuditOptions();
            configure?.Invoke(options);

            Writer = Substitute.For<IAuditWriter>();
            Writer.WriteAsync(Arg.Do<AuditLog>(a => Entries.Add(a)), Arg.Any<CancellationToken>())
                .Returns(Task.CompletedTask);

            var clock = Substitute.For<ISystemClock>();
            clock.UtcNow.Returns(new DateTimeOffset(2026, 4, 22, 10, 0, 0, TimeSpan.Zero));

            var interceptor = new AuditSaveChangesInterceptor(
                Writer,
                tenant,
                user,
                clock,
                Options.Create(options),
                NullLogger<AuditSaveChangesInterceptor>.Instance);

            var opts = new DbContextOptionsBuilder<TestDbContext>()
                .UseInMemoryDatabase($"audit-tests-{Guid.NewGuid():N}")
                .AddInterceptors(interceptor)
                .Options;
            DbContext = new TestDbContext(opts);
        }
    }

    private sealed class Widget : IAggregateRoot<Guid>, IMultiTenant
    {
        public Guid Id { get; init; }
        public string? TenantId { get; init; }
        public string Name { get; set; } = string.Empty;

        public IReadOnlyCollection<IDomainEvent> DomainEvents { get; } = [];
        public void ClearDomainEvents() { }
    }

    private sealed class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options)
    {
        public DbSet<Widget> Widgets => Set<Widget>();
        public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new AuditLogConfiguration());
            modelBuilder.Entity<Widget>().HasKey(x => x.Id);
        }
    }
}
