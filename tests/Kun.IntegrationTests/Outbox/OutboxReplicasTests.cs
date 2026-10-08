using Kun.Core.MultiTenancy;
using Kun.EntityFrameworkCore;
using Kun.EntityFrameworkCore.DependencyInjection;
using Kun.EventBus;
using Kun.EventBus.Background;
using Kun.EventBus.DependencyInjection;
using Kun.EventBus.Outbox;
using Kun.EventBus.RabbitMQ;
using Kun.EventBus.RabbitMQ.DependencyInjection;
using Kun.EventBus.RabbitMQ.Options;
using Kun.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using Xunit;

namespace Kun.IntegrationTests.Outbox;

internal sealed class PublisherDbContext(DbContextOptions<PublisherDbContext> options)
    : KunDbContext(options, NullTenantContext.Instance);

public sealed class OutboxReplicasTests(PostgresFixture postgres, RabbitMqFixture rabbit)
    : IClassFixture<PostgresFixture>, IClassFixture<RabbitMqFixture>
{
    private ServiceProvider Build(TimeProvider? clock = null, IIntegrationEventPublisher? publisher = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(clock ?? TimeProvider.System);
        services.AddDbContext<PublisherDbContext>(options => options.UseNpgsql(postgres.ConnectionString));
        services.AddScoped<KunDbContext>(sp => sp.GetRequiredService<PublisherDbContext>());
        services.AddKunEntityFrameworkCore();
        services.AddKunEventBus();
        services.AddSingleton<IOptions<RabbitMqOptions>>(Options.Create(new RabbitMqOptions
        {
            ConnectionString = rabbit.ConnectionString,
            ConsumerName = "publisher-replicas"
        }));
        if (publisher is not null) services.AddSingleton(publisher);
        services.AddRabbitMq(_ => { });
        services.AddSingleton<IOptions<OutboxPublisherOptions>>(Options.Create(new OutboxPublisherOptions { BatchSize = 5 }));
        services.AddKunOutboxPublisher();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private static OutboxPublisherBackgroundService Worker(ServiceProvider provider) =>
        provider.GetServices<IHostedService>().OfType<OutboxPublisherBackgroundService>().Single();

    [Fact]
    public async Task TwoPublishers_DefaultRowLocks_PublishEveryMessageOnceWithConfirms()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var first = Build();
        await using var second = Build();
        var eventType = "replicas.test.v1";
        var connection = await first.GetRequiredService<RabbitMqConnectionProvider>().GetConnectionAsync(ct);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: ct);
        await channel.ExchangeDeclareAsync("kun.events", "topic", durable: true, cancellationToken: ct);
        var queue = "replicas." + Guid.NewGuid().ToString("N");
        await channel.QueueDeclareAsync(queue, durable: true, exclusive: false, autoDelete: false, cancellationToken: ct);
        await channel.QueueBindAsync(queue, "kun.events", eventType, cancellationToken: ct);
        await using (var setup = first.CreateAsyncScope())
        {
            var db = setup.ServiceProvider.GetRequiredService<PublisherDbContext>();
            await db.Database.EnsureCreatedAsync(ct);
            await db.OutboxMessages.ExecuteDeleteAsync(ct);
            for (var i = 0; i < 40; i++) db.OutboxMessages.Add(new OutboxMessage(Guid.NewGuid(), eventType, "{}", null, null, null, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync(ct);
        }
        async Task Poll(ServiceProvider replica)
        {
            for (var i = 0; i < 10; i++) await Worker(replica).ProcessBatchAsync(ct);
        }
        await Task.WhenAll(Poll(first), Poll(second));
        var delivered = new List<string>();
        while (await channel.BasicGetAsync(queue, autoAck: true, ct) is { } message)
            delivered.Add(message.BasicProperties.MessageId!);
        Assert.Equal(40, delivered.Count);
        Assert.Equal(40, delivered.Distinct().Count());
        await using var verify = first.CreateAsyncScope();
        Assert.Equal(40, await verify.ServiceProvider.GetRequiredService<PublisherDbContext>().OutboxMessages
            .CountAsync(m => m.Status == OutboxStatus.Published, ct));
    }

    [Fact]
    public async Task FailedPublish_PersistsExponentialRetryAcrossScopes_AndDoesNotPollEarly()
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new Clock();
        var failing = new FailingPublisher();
        await using var services = Build(clock, failing);
        await using (var setup = services.CreateAsyncScope())
        {
            var db = setup.ServiceProvider.GetRequiredService<PublisherDbContext>();
            await db.Database.EnsureCreatedAsync(ct);
            await db.OutboxMessages.ExecuteDeleteAsync(ct);
            db.OutboxMessages.Add(new OutboxMessage(Guid.NewGuid(), "retry.test.v1", "{}", null, null, null, clock.GetUtcNow()));
            await db.SaveChangesAsync(ct);
        }
        await Worker(services).ProcessBatchAsync(ct);
        await Worker(services).ProcessBatchAsync(ct);
        Assert.Equal(1, failing.Attempts);
        clock.Now += TimeSpan.FromSeconds(1);
        await Worker(services).ProcessBatchAsync(ct);
        Assert.Equal(2, failing.Attempts);
        await using var verify = services.CreateAsyncScope();
        var row = await verify.ServiceProvider.GetRequiredService<PublisherDbContext>().OutboxMessages.SingleAsync(ct);
        Assert.Equal(clock.GetUtcNow().AddSeconds(2), row.NextAttemptAt);
        Assert.Equal(2, row.AttemptCount);
        Assert.Equal(OutboxStatus.Pending, row.Status);
        Assert.Equal("broker unavailable", row.LastError);
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 10, 9, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class FailingPublisher : IIntegrationEventPublisher
    {
        public int Attempts;
        public ValueTask PublishAsync(OutboxMessage message, CancellationToken cancellationToken = default)
        {
            Attempts++;
            throw new InvalidOperationException("broker unavailable");
        }
    }
}
