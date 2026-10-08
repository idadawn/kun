using Kun.Core.Domain;
using Kun.Core.MultiTenancy;
using Kun.EntityFrameworkCore;
using Kun.EntityFrameworkCore.DependencyInjection;
using Kun.EntityFrameworkCore.Extensions;
using Kun.EventBus.DependencyInjection;
using Kun.IntegrationTests.Fixtures;
using Kun.Mediator;
using Kun.Mediator.DependencyInjection;
using Kun.Sample.Catalog.Application.Commands;
using Kun.Sample.Catalog.Application.DomainEventHandlers;
using Kun.Sample.Catalog.Contracts;
using Kun.Sample.Catalog.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Kun.IntegrationTests.Outbox;

public sealed class CatalogOutboxTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CreateProduct_ThroughActualCatalogHandler_PersistsNotificationOutboxBeforeCommit(bool reject)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ITenantContext>(NullTenantContext.Instance);
        services.AddScoped<IDomainEventDispatcher, MediatorDomainEventDispatcher>();
        services.AddDbContext<CatalogDbContext>(options => options.UseNpgsql(postgres.ConnectionString).UseKunSchema("catalog"));
        services.AddScoped<KunDbContext>(sp => sp.GetRequiredService<CatalogDbContext>());
        services.AddKunEntityFrameworkCore();
        services.AddKunEventBus(builder => builder.Register<ProductPublishedIntegrationEvent>("product.published.v1"));
        services.AddKunMediator(builder =>
        {
            builder.AddRequestHandler<CreateProductCommand, Guid, CreateProductCommandHandler>()
                .AddNotificationHandler<ProductCreatedNotification, ProductCreatedHandler>();
            if (reject) builder.AddNotificationHandler<ProductCreatedNotification, RejectNotification>();
        });
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using (var setup = provider.CreateAsyncScope())
        {
            var initial = setup.ServiceProvider.GetRequiredService<CatalogDbContext>();
            await initial.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
            await initial.Products.ExecuteDeleteAsync(TestContext.Current.CancellationToken);
            await initial.OutboxMessages.ExecuteDeleteAsync(TestContext.Current.CancellationToken);
        }
        if (reject)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await provider.GetRequiredService<ISender>().Send(
                new CreateProductCommand("不能提交", "通知失败", 10, Guid.NewGuid(), 1), TestContext.Current.CancellationToken));
            await using var failed = provider.CreateAsyncScope();
            var failedDb = failed.ServiceProvider.GetRequiredService<CatalogDbContext>();
            Assert.Equal(0, await failedDb.Products.CountAsync(TestContext.Current.CancellationToken));
            Assert.Equal(0, await failedDb.OutboxMessages.CountAsync(TestContext.Current.CancellationToken));
            return;
        }
        var id = await provider.GetRequiredService<ISender>().Send(
            new CreateProductCommand("验收商品", "同事务通知", 10, Guid.NewGuid(), 1), TestContext.Current.CancellationToken);
        await using var verify = provider.CreateAsyncScope();
        var db = verify.ServiceProvider.GetRequiredService<CatalogDbContext>();
        Assert.True(await db.Products.AnyAsync(p => p.Id == id, TestContext.Current.CancellationToken));
        Assert.Equal(1, await db.OutboxMessages.CountAsync(m => m.EventTypeId == "product.published.v1", TestContext.Current.CancellationToken));
    }
    private sealed class RejectNotification : INotificationHandler<ProductCreatedNotification>
    {
        public ValueTask Handle(ProductCreatedNotification notification, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("通知失败，整个业务保存必须失败");
    }

}
