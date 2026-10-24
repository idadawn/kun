using Kun.Core.Domain;
using Kun.Core.Security;
using Kun.EntityFrameworkCore.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Kun.EntityFrameworkCore.Tests;

// 数据约定不占当前主体端口：身份适配器在数据约定之前或之后以 TryAdd 登记都生效，没有适配器时按匿名写 null。
public sealed class DataConventionRegistrationTests
{
    [Fact]
    public void AddKunDataConventions_DoesNotRegisterCurrentUser()
    {
        var services = new ServiceCollection();
        services.AddKunDataConventions<RowsContext>();
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(ICurrentUser));
    }

    [Fact]
    public async Task Stamp_AdapterRegisteredAfterConventions_UsesAdapterSubject()
    {
        var services = Services();
        services.AddKunDataConventions<RowsContext>();
        services.TryAddScoped<ICurrentUser>(_ => new User("late-adapter"));
        Assert.Equal("late-adapter", await SaveAsync(services));
    }

    [Fact]
    public async Task Stamp_AdapterRegisteredBeforeConventions_UsesAdapterSubject()
    {
        var services = Services();
        services.TryAddScoped<ICurrentUser>(_ => new User("early-adapter"));
        services.AddKunDataConventions<RowsContext>();
        Assert.Equal("early-adapter", await SaveAsync(services));
    }

    [Fact]
    public async Task Stamp_WithoutAdapter_WritesNullSubject()
    {
        var services = Services();
        services.AddKunDataConventions<RowsContext>();
        Assert.Null(await SaveAsync(services));
    }

    private static ServiceCollection Services()
    {
        var services = new ServiceCollection();
        var database = Guid.NewGuid().ToString();
        services.AddDbContext<RowsContext>(options => options.UseInMemoryDatabase(database));
        return services;
    }

    private static async Task<string?> SaveAsync(ServiceCollection services)
    {
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RowsContext>();
        var row = new Row { Id = Guid.NewGuid() };
        db.Add(row);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        Assert.NotEqual(default, row.CreatedAt);
        return row.CreatedBy;
    }

    private sealed class User(string id) : ICurrentUser
    {
        public string? UserId => id;
        public bool IsAuthenticated => true;
        public IReadOnlyList<string> Roles => [];
    }

    private sealed class RowsContext(DbContextOptions<RowsContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.Entity<Row>();
    }

    public sealed class Row : IAuditedObject<string>
    {
        public Guid Id { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public string? CreatedBy { get; set; }
        public string? UpdatedBy { get; set; }
    }
}
