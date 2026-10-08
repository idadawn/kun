using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Kun.EventBus.Background;
using Kun.EventBus.DependencyInjection;
using Kun.EventBus.Outbox;
using Kun.EventBus.RabbitMQ.DependencyInjection;
using Kun.EventBus.RabbitMQ.Options;
using Kun.IntegrationTests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using Xunit;

namespace Kun.IntegrationTests.EventBus;

// ---------------------------------------------------------------------------
// Internal types — source generator requires non-file-scoped visibility
// ---------------------------------------------------------------------------

internal sealed record ResilienceTestEvent(Guid Id);

[JsonSerializable(typeof(ResilienceTestEvent))]
internal sealed partial class ResilienceJsonContext : JsonSerializerContext { }

/// <summary>
/// In-memory <see cref="IOutboxMessageStore"/> for driving
/// <see cref="OutboxPublisherBackgroundService"/> without EF Core.
/// </summary>
internal sealed class InMemoryOutboxMessageStore : IOutboxMessageStore
{
    private readonly List<OutboxMessage> _messages = [];
    private readonly object _lock = new();

    public void Add(OutboxMessage message)
    {
        lock (_lock) { _messages.Add(message); }
    }

    public Task<IReadOnlyList<OutboxMessage>> FetchPendingAsync(int batchSize, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            IReadOnlyList<OutboxMessage> pending = _messages
                .Where(m => m.Status == OutboxStatus.Pending)
                .Take(batchSize)
                .ToList();
            return Task.FromResult(pending);
        }
    }

    public Task UpdateAsync(OutboxMessage message, CancellationToken cancellationToken = default) =>
        Task.CompletedTask; // state already mutated on the shared object reference
}

// ---------------------------------------------------------------------------
// Test class
// ---------------------------------------------------------------------------

public sealed class OutboxPublisherResilienceTests : IAsyncLifetime
{
    private readonly RabbitMqFixture _rabbitmq = new();

    public async ValueTask InitializeAsync() =>
        await _rabbitmq.InitializeAsync();

    public async ValueTask DisposeAsync() =>
        await _rabbitmq.DisposeAsync();

    [Fact]
    public async Task BadConnection_MessageRemainsNotPublished_GoodConnection_MessagePublishes()
    {
        var ct = TestContext.Current.CancellationToken;

        if (string.IsNullOrEmpty(_rabbitmq.ConnectionString))
        {
            Assert.Skip("Docker not available.");
            return;
        }

        // ----------------------------------------------------------------
        // Phase 1: bad connection — outbox publisher must fail to publish.
        // We use a very short poll interval so the background service fires
        // quickly, then observe the message status.
        // ----------------------------------------------------------------
        var badStore = new InMemoryOutboxMessageStore();
        var badEventId = Guid.NewGuid();
        var badMessage = new OutboxMessage(
            id: badEventId,
            eventTypeId: "integration.test.resilience.v1",
            payload: JsonSerializer.Serialize(
                new ResilienceTestEvent(badEventId),
                ResilienceJsonContext.Default.ResilienceTestEvent),
            tenantId: null,
            traceParent: null,
            traceState: null,
            occurredAt: DateTimeOffset.UtcNow);
        badStore.Add(badMessage);

        using (var badHost = BuildHost("amqp://bad-host:5672/", "test-resilience-bad", badStore,
                   pollInterval: TimeSpan.FromMilliseconds(200), maxAttempts: 1))
        {
            await badHost.StartAsync(ct);
            // Give the background service two poll cycles to attempt publish.
            await Task.Delay(TimeSpan.FromSeconds(2), ct);
            await badHost.StopAsync(CancellationToken.None);
        }

        // InMemoryOutboxMessageStore.UpdateAsync is a no-op — the background service
        // mutates the shared OutboxMessage instance in place, so badMessage reflects
        // the updated status/attempt count directly.
        badMessage.Status.Should().NotBe(OutboxStatus.Published,
            "a bad connection string must not result in Published status");
        badMessage.AttemptCount.Should().BeGreaterThan(0,
            "at least one publish attempt must be recorded");

        // ----------------------------------------------------------------
        // Phase 2: good connection — message must be published successfully.
        // ----------------------------------------------------------------
        var goodStore = new InMemoryOutboxMessageStore();
        var goodEventId = Guid.NewGuid();
        var goodMessage = new OutboxMessage(
            id: goodEventId,
            eventTypeId: "integration.test.resilience.v1",
            payload: JsonSerializer.Serialize(
                new ResilienceTestEvent(goodEventId),
                ResilienceJsonContext.Default.ResilienceTestEvent),
            tenantId: null,
            traceParent: null,
            traceState: null,
            occurredAt: DateTimeOffset.UtcNow);
        goodStore.Add(goodMessage);

        using (var goodHost = BuildHost(_rabbitmq.ConnectionString, "test-resilience-good", goodStore,
                   pollInterval: TimeSpan.FromMilliseconds(200), maxAttempts: 3))
        {
            var factory = new ConnectionFactory { Uri = new Uri(_rabbitmq.ConnectionString) };
            await using var connection = await factory.CreateConnectionAsync(ct);
            await using var channel = await connection.CreateChannelAsync(cancellationToken: ct);
            await channel.ExchangeDeclareAsync("kun.events", "topic", durable: true, cancellationToken: ct);
            var queue = "resilience." + Guid.NewGuid().ToString("N");
            await channel.QueueDeclareAsync(queue, durable: true, exclusive: false, autoDelete: false, cancellationToken: ct);
            await channel.QueueBindAsync(queue, "kun.events", "integration.test.resilience.v1", cancellationToken: ct);
            await goodHost.StartAsync(ct);
            // Give the background service time to publish.
            await Task.Delay(TimeSpan.FromSeconds(3), ct);
            await goodHost.StopAsync(CancellationToken.None);
        }

        goodMessage.Status.Should().Be(OutboxStatus.Published,
            "a working RabbitMQ connection must result in Published status");
    }

    private static IHost BuildHost(
        string connectionString,
        string consumerName,
        InMemoryOutboxMessageStore store,
        TimeSpan pollInterval,
        int maxAttempts)
    {
        return Host.CreateDefaultBuilder()
            .ConfigureLogging(l => l.SetMinimumLevel(LogLevel.Warning))
            .ConfigureServices(services =>
            {
                services.AddKunEventBus(b =>
                {
                    b.Register<ResilienceTestEvent>("integration.test.resilience.v1");
                    b.UseInMemoryIdempotency();
                });

                // Register RabbitMq transport (no subscriptions needed for publisher tests).
                services.AddRabbitMq(_ => { });

                // Override IOptions<RabbitMqOptions> with object-initializer to satisfy
                // required init-only properties.
                services.AddSingleton<IOptions<RabbitMqOptions>>(
                    Options.Create(new RabbitMqOptions
                    {
                        ConnectionString = connectionString,
                        ConsumerName = consumerName,
                    }));

                services.AddSingleton<IOutboxMessageStore>(store);
                services.AddSingleton<IOutboxLockProvider>(NullOutboxLockProvider.Instance); // 单实例内存替身显式无锁。

                // Register outbox publisher with short poll interval for tests.
                services.AddKunOutboxPublisher();

                // Override IOptions<OutboxPublisherOptions> similarly.
                services.AddSingleton<IOptions<OutboxPublisherOptions>>(
                    Options.Create(new OutboxPublisherOptions
                    {
                        BatchSize = 10,
                        PollInterval = pollInterval,
                        MaxAttempts = maxAttempts,
                    }));
            })
            .Build();
    }
}
