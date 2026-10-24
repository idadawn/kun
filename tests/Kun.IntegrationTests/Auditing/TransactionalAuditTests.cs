using System.Security.Claims;
using System.Text.Json;
using Kun.Audit;
using Kun.Audit.Abstractions;
using Kun.Audit.DependencyInjection;
using Kun.Audit.EntityFrameworkCore.DependencyInjection;
using Kun.Auditing;
using Kun.Auditing.DependencyInjection;
using Kun.Core.Domain;
using Kun.Core.MultiTenancy;
using Kun.Core.Results;
using Kun.Core.Security;
using Kun.EntityFrameworkCore;
using Kun.EntityFrameworkCore.Conventions;
using Kun.EntityFrameworkCore.DependencyInjection;
using Kun.EntityFrameworkCore.Extensions;
using Kun.IntegrationTests.Fixtures;
using Kun.Security.DependencyInjection;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Kun.IntegrationTests.Auditing;

internal sealed class TransactionalOrder : AggregateRoot<Guid, Guid?>, IMultiTenant
{
    public string? TenantId { get; private set; }
    public string Name { get; private set; } = "";
    public static TransactionalOrder Create(string tenant, string name) => new() { TenantId = tenant, Name = name };
    public void Rename(string name) => Name = name;
}

internal sealed class TransactionalAuditDbContext(DbContextOptions<TransactionalAuditDbContext> options, ITenantContext tenant)
    : KunDbContext(options, tenant)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TransactionalOrder>(entity =>
        {
            entity.ToTable("transactional_orders");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).IsRequired();
            entity.ToTable(table => table.HasCheckConstraint("valid_name", "name <> 'reject'"));
        });
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyKunDataConventions();
    }
}

public sealed class TransactionalAuditTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _postgres;
    public TransactionalAuditTests(PostgresFixture postgres) => _postgres = postgres;

    private ServiceProvider Build(string tenantId, bool authenticated = true)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ITenantContext>(new Tenant(tenantId));
        services.AddSingleton<ICurrentUser>(new User(authenticated));
        services.AddDbContext<TransactionalAuditDbContext>(options => options.UseNpgsql(_postgres.ConnectionString));
        services.AddKunDataConventions<TransactionalAuditDbContext>();
        services.AddKunAuditing<TransactionalAuditDbContext>();
        services.AddAuditable<TransactionalOrder>();
        services.AddKunTenantRls<TransactionalAuditDbContext>();
        services.AddKunAudit().UseEntityFramework<TransactionalAuditDbContext>();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    [Fact]
    public async Task Save_BusinessAndAuditAndOutbox_CommitOrRollbackTogether()
    {
        var tenant = Guid.NewGuid().ToString();
        await using var services = Build(tenant);
        Guid committedId;
        Guid rolledBackId;
        await using (var scope = services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TransactionalAuditDbContext>();
            await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
            var committed = TransactionalOrder.Create(tenant, "committed");
            db.Add(committed);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            committedId = committed.Id;
            Assert.Equal('7', committed.Id.ToString()[14]);
            Assert.NotEqual(default, committed.CreatedAt);
            Assert.Equal(User.Id, committed.CreatedBy);
            await using var tx = await db.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
            var rolledBack = TransactionalOrder.Create(tenant, "rolled-back");
            db.Add(rolledBack);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            rolledBackId = rolledBack.Id;
            await tx.RollbackAsync(TestContext.Current.CancellationToken);
        }
        await using var verifyScope = services.CreateAsyncScope();
        var verify = verifyScope.ServiceProvider.GetRequiredService<TransactionalAuditDbContext>();
        Assert.True(await verify.Set<TransactionalOrder>().AnyAsync(x => x.Id == committedId, TestContext.Current.CancellationToken));
        Assert.False(await verify.Set<TransactionalOrder>().AnyAsync(x => x.Id == rolledBackId, TestContext.Current.CancellationToken));
        var log = await verify.Set<AuditLog>().SingleAsync(x => x.ResourceId == committedId.ToString(), TestContext.Current.CancellationToken);
        Assert.Equal(tenant, log.TenantId);
        Assert.Equal(User.Id.ToString(), log.ActorId);
        Assert.Equal(AuditActorType.User, log.ActorType);
        Assert.False(await verify.Set<AuditLog>().AnyAsync(x => x.ResourceId == rolledBackId.ToString(), TestContext.Current.CancellationToken));
        var message = await verify.OutboxMessages.SingleAsync(x => x.Id == log.Id, TestContext.Current.CancellationToken);
        Assert.Equal(AuditingServiceCollectionExtensions.AuditEventV2StableId, message.EventTypeId);
        using var payload = JsonDocument.Parse(message.Payload);
        Assert.Equal(tenant, payload.RootElement.GetProperty("tenantId").GetString());
        Assert.Equal(User.Id.ToString(), payload.RootElement.GetProperty("userId").GetString());
        Assert.False(await verify.OutboxMessages.AnyAsync(x => x.Payload.Contains(rolledBackId.ToString()), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Save_FailureThenRetry_ProducesExactlyOneAudit()
    {
        var tenant = Guid.NewGuid().ToString();
        await using var services = Build(tenant);
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TransactionalAuditDbContext>();
        await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        var order = TransactionalOrder.Create(tenant, "reject");
        db.Add(order);
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(TestContext.Current.CancellationToken));
        Assert.Empty(db.ChangeTracker.Entries<AuditLog>());
        order.Rename("retry");
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, await db.Set<AuditLog>().CountAsync(x => x.ResourceId == order.Id.ToString(), TestContext.Current.CancellationToken));
        Assert.Equal(1, await db.OutboxMessages.CountAsync(x => x.Payload.Contains(order.Id.ToString()), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Save_AuditInsertFailure_RollsBackBusinessAndOutbox()
    {
        var tenant = Guid.NewGuid().ToString();
        await using var services = Build(tenant);
        Guid id;
        await using (var scope = services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TransactionalAuditDbContext>();
            await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
            scope.ServiceProvider.GetRequiredService<AuditContext>().ActorType = new string('x', 33);
            var order = TransactionalOrder.Create(tenant, "audit-rejected");
            db.Add(order);
            id = order.Id;
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(TestContext.Current.CancellationToken));
            Assert.Empty(db.ChangeTracker.Entries<AuditLog>());
        }
        await using var verifyScope = services.CreateAsyncScope();
        var verify = verifyScope.ServiceProvider.GetRequiredService<TransactionalAuditDbContext>();
        Assert.False(await verify.Set<TransactionalOrder>().AnyAsync(x => x.Id == id, TestContext.Current.CancellationToken));
        Assert.False(await verify.Set<AuditLog>().AnyAsync(x => x.ResourceId == id.ToString(), TestContext.Current.CancellationToken));
        Assert.False(await verify.OutboxMessages.AnyAsync(x => x.Payload.Contains(id.ToString()), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Save_NullActorAndUpdates_PreservesCreationFields()
    {
        var tenant = Guid.NewGuid().ToString();
        await using var services = Build(tenant, authenticated: false);
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TransactionalAuditDbContext>();
        await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        var order = TransactionalOrder.Create(tenant, "initial");
        db.Add(order);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        Assert.Null(order.CreatedBy);
        await db.Entry(order).ReloadAsync(TestContext.Current.CancellationToken);
        var createdAt = order.CreatedAt;
        db.Entry(order).Property(x => x.CreatedAt).CurrentValue = DateTime.UtcNow.AddDays(100);
        db.Entry(order).Property(x => x.CreatedBy).CurrentValue = Guid.NewGuid();
        order.Rename("updated");
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        await db.Entry(order).ReloadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(createdAt, order.CreatedAt);
        Assert.Null(order.CreatedBy);
        Assert.NotNull(order.UpdatedAt);
        Assert.Null(order.UpdatedBy);
    }

    [Fact]
    public async Task Save_AuthenticatedUpdate_StampsUpdaterAndPreservesCreator()
    {
        var tenant = Guid.NewGuid().ToString();
        await using var services = Build(tenant);
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TransactionalAuditDbContext>();
        await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        var order = TransactionalOrder.Create(tenant, "created");
        db.Add(order);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        await db.Entry(order).ReloadAsync(TestContext.Current.CancellationToken);
        var createdAt = order.CreatedAt;
        order.Rename("changed");
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        await db.Entry(order).ReloadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(createdAt, order.CreatedAt);
        Assert.Equal(User.Id, order.CreatedBy);
        Assert.Equal(User.Id, order.UpdatedBy);
        Assert.True(order.UpdatedAt >= createdAt);
    }

    [Fact]
    public async Task Save_ConcurrentUpdates_ThrowsAndMapsConflict()
    {
        var tenant = Guid.NewGuid().ToString();
        await using var services = Build(tenant);
        Guid id;
        await using (var seedScope = services.CreateAsyncScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<TransactionalAuditDbContext>();
            await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
            var order = TransactionalOrder.Create(tenant, "initial");
            db.Add(order);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            id = order.Id;
        }
        await using var first = services.CreateAsyncScope();
        await using var second = services.CreateAsyncScope();
        var one = first.ServiceProvider.GetRequiredService<TransactionalAuditDbContext>();
        var two = second.ServiceProvider.GetRequiredService<TransactionalAuditDbContext>();
        var a = await one.Set<TransactionalOrder>().SingleAsync(x => x.Id == id, TestContext.Current.CancellationToken);
        var b = await two.Set<TransactionalOrder>().SingleAsync(x => x.Id == id, TestContext.Current.CancellationToken);
        a.Rename("winner");
        b.Rename("loser");
        await one.SaveChangesAsync(TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => two.SaveChangesAsync(TestContext.Current.CancellationToken));
        var result = await two.SaveChangesResultAsync(TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        await using var verifyScope = services.CreateAsyncScope();
        var verify = verifyScope.ServiceProvider.GetRequiredService<TransactionalAuditDbContext>();
        Assert.Equal("winner", (await verify.Set<TransactionalOrder>().SingleAsync(x => x.Id == id, TestContext.Current.CancellationToken)).Name);
        Assert.Equal(2, await verify.Set<AuditLog>().CountAsync(x => x.ResourceId == id.ToString(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AuditReader_EnforcesTenantAndDatabaseFilters()
    {
        var tenant = Guid.NewGuid().ToString();
        await using var services = Build(tenant);
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TransactionalAuditDbContext>();
        await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        scope.ServiceProvider.GetRequiredService<AuditContext>().ActorType = AuditActorType.Agent;
        var order = TransactionalOrder.Create(tenant, "agent-action");
        db.Add(order);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var reader = scope.ServiceProvider.GetRequiredService<IAuditReader>();
        var found = new List<AuditLog>();
        await foreach (var log in reader.QueryAsync(new AuditQuery(tenant, ActorId: User.Id.ToString(), ResourceId: order.Id.ToString(), Limit: 1), TestContext.Current.CancellationToken))
            found.Add(log);
        Assert.Equal(AuditActorType.Agent, Assert.Single(found).ActorType);
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in reader.QueryAsync(new AuditQuery("other-tenant"), TestContext.Current.CancellationToken)) { }
        });
    }

    // 数据约定与实体审计不占租户和当前主体端口：宿主之后才以 TryAdd 登记的两个端口照样生效。
    [Fact]
    public async Task Save_PortsRegisteredAfterDataComponents_StampTenantAndActor()
    {
        var tenant = Guid.NewGuid().ToString();
        var services = DataComponentsFirst();
        services.TryAddSingleton<ITenantContext>(new Tenant(tenant));
        services.TryAddScoped<ICurrentUser>(_ => new User(true));
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await AssertStampedAsync(provider, tenant, User.Id);
    }

    // 先登记数据组件、再 AddKunSecurity：JwtCurrentUser 与按令牌取租户的上下文生效，审计字段与审计日志记令牌主体与租户。
    [Fact]
    public async Task Save_KunSecurityRegisteredAfterDataComponents_StampsTokenSubjectAndTenant()
    {
        var tenant = Guid.NewGuid().ToString();
        var subject = Guid.NewGuid();
        var services = DataComponentsFirst();
        services.AddKunSecurity();
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        provider.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", subject.ToString()), new Claim("tenant_id", tenant)], "test"))
        };
        await AssertStampedAsync(provider, tenant, subject);
    }

    private ServiceCollection DataComponentsFirst()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<TransactionalAuditDbContext>(options => options.UseNpgsql(_postgres.ConnectionString));
        services.AddKunDataConventions<TransactionalAuditDbContext>();
        services.AddKunAuditing<TransactionalAuditDbContext>();
        services.AddAuditable<TransactionalOrder>();
        services.AddKunTenantRls<TransactionalAuditDbContext>();
        return services;
    }

    private static async Task AssertStampedAsync(ServiceProvider provider, string tenant, Guid actor)
    {
        Guid id;
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TransactionalAuditDbContext>();
            await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
            var order = TransactionalOrder.Create(tenant, "ports-registered-late");
            db.Add(order);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            Assert.Equal(actor, order.CreatedBy);
            id = order.Id;
        }
        await using var verifyScope = provider.CreateAsyncScope();
        var verify = verifyScope.ServiceProvider.GetRequiredService<TransactionalAuditDbContext>();
        var log = await verify.Set<AuditLog>().SingleAsync(x => x.ResourceId == id.ToString(), TestContext.Current.CancellationToken);
        Assert.Equal(tenant, log.TenantId);
        Assert.Equal(actor.ToString(), log.ActorId);
    }

    private sealed class Tenant(string id) : ITenantContext
    {
        public string? TenantId => id;
        public bool IsMultiTenant => true;
    }
    private sealed class User(bool authenticated) : ICurrentUser
    {
        public static readonly Guid Id = Guid.Parse("aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee");
        public string? UserId => authenticated ? Id.ToString() : null;
        public IReadOnlyList<string> Roles => [];
        public bool IsAuthenticated => authenticated;
    }
}
