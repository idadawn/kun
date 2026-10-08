using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Kun.EventBus;
using Kun.EventBus.DependencyInjection;
using Kun.EventBus.Idempotency;
using Kun.EventBus.Outbox;
using Kun.EventBus.RabbitMQ;
using Kun.EventBus.RabbitMQ.Consuming;
using Kun.EventBus.RabbitMQ.DependencyInjection;
using Kun.EventBus.RabbitMQ.Options;
using Kun.IntegrationTests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using StackExchange.Redis;
using Xunit;

namespace Kun.IntegrationTests.EventBus;

// ---------------------------------------------------------------------------
// Internal types — JSON source generator requires non-file-scoped visibility
// ---------------------------------------------------------------------------

internal sealed record IdempotencyTestEvent(Guid Id, string Data);

internal sealed class CountingHandler : IIntegrationEventHandler<IdempotencyTestEvent>
{
    public int InvocationCount;
    public int Attempts;
    public int FailuresRemaining;
    public TaskCompletionSource? FirstAttemptGate;

    public async ValueTask HandleAsync(IdempotencyTestEvent @event, MessageContext context, CancellationToken cancellationToken)
    {
        var attempt = Interlocked.Increment(ref Attempts);
        if (attempt == 1 && FirstAttemptGate is not null) await FirstAttemptGate.Task.WaitAsync(cancellationToken);
        if (Interlocked.Decrement(ref FailuresRemaining) >= 0) throw new InvalidOperationException("首次处理失败");
        Interlocked.Increment(ref InvocationCount);
    }
}

[JsonSerializable(typeof(IdempotencyTestEvent))]
internal sealed partial class IdempotencyJsonContext : JsonSerializerContext { }

// ---------------------------------------------------------------------------
// Test class
// ---------------------------------------------------------------------------

public sealed class IdempotencyTests : IAsyncLifetime
{
    private readonly RabbitMqFixture _rabbitmq = new() { PreservePortAcrossRestarts = true };
    private readonly RedisFixture _redis = new();
    private IHost? _host;
    private CountingHandler? _handler;

    public async ValueTask InitializeAsync()
    {
        await Task.WhenAll(
            _rabbitmq.InitializeAsync().AsTask(),
            _redis.InitializeAsync().AsTask());

        if (string.IsNullOrEmpty(_rabbitmq.ConnectionString) || string.IsNullOrEmpty(_redis.ConnectionString))
        {
            Assert.Skip("Docker not available.");
            return;
        }

        _handler = new CountingHandler();
        var capturedHandler = _handler;

        _host = Host.CreateDefaultBuilder()
            .ConfigureLogging(l => l.SetMinimumLevel(LogLevel.Warning))
            .ConfigureServices(services =>
            {
                services.AddSingleton<IOptions<RabbitMqOptions>>(
                    Options.Create(new RabbitMqOptions
                    {
                        ConnectionString = _rabbitmq.ConnectionString,
                        ConsumerName = "test-consumer-idempotency",
                        RetryBaseDelay = TimeSpan.FromMilliseconds(100),
                    }));

                services.AddKunEventBus(b =>
                {
                    b.Register<IdempotencyTestEvent>("integration.test.idempotency.v1");
                    b.UseRedisIdempotency();
                });

                services.AddSingleton<IConnectionMultiplexer>(
                    _ => ConnectionMultiplexer.Connect(_redis.ConnectionString));

                services.AddRabbitMq(
                    _ => { },
                    builder => builder.Subscribe<IdempotencyTestEvent, CountingHandler>(
                        "integration.test.idempotency.v1",
                        IdempotencyJsonContext.Default.IdempotencyTestEvent));

                services.AddScoped<IIntegrationEventHandler<IdempotencyTestEvent>>(_ => capturedHandler);
            })
            .Build();

        var connection = await _host.Services.GetRequiredService<RabbitMqConnectionProvider>().GetConnectionAsync(TestContext.Current.CancellationToken);
        await using (var channel = await connection.CreateChannelAsync(cancellationToken: TestContext.Current.CancellationToken))
        {
            await channel.ExchangeDeclareAsync("kun.events", "topic", durable: true, cancellationToken: TestContext.Current.CancellationToken);
            await channel.QueueDeclareAsync("test-consumer-idempotency.integration.test.idempotency.v1", durable: true,
                exclusive: false, autoDelete: false, arguments: DeadLetterHandler.GetQueueArguments("kun.events"), cancellationToken: TestContext.Current.CancellationToken);
            await channel.QueueBindAsync("test-consumer-idempotency.integration.test.idempotency.v1", "kun.events", "integration.test.idempotency.v1", cancellationToken: TestContext.Current.CancellationToken);
        }
        await _host.StartAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_host is not null)
        {
            await _host.StopAsync(CancellationToken.None);
            _host.Dispose();
        }

        await _rabbitmq.DisposeAsync();
        await _redis.DisposeAsync();
    }

    [Fact]
    public async Task SameMessageId_PublishedTwice_HandlerInvokedOnlyOnce()
    {
        var ct = TestContext.Current.CancellationToken;

        if (_host is null)
        {
            Assert.Skip("Docker not available.");
            return;
        }

        var messageId = Guid.NewGuid();
        var payload = JsonSerializer.Serialize(
            new IdempotencyTestEvent(messageId, "dedup-test"),
            IdempotencyJsonContext.Default.IdempotencyTestEvent);

        var publisher = _host.Services.GetRequiredService<IIntegrationEventPublisher>();
        var message = new OutboxMessage(
            id: messageId,
            eventTypeId: "integration.test.idempotency.v1",
            payload: payload,
            tenantId: null,
            traceParent: null,
            traceState: null,
            occurredAt: DateTimeOffset.UtcNow);

        // Publish once, wait for handler to process and mark it.
        await publisher.PublishAsync(message, ct);

        // Poll up to 10s for the first delivery to land.
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (_handler!.InvocationCount == 0 && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(100, ct);
        }

        _handler.InvocationCount.Should().Be(1, "first delivery must invoke the handler");

        // Publish the SAME message ID a second time — idempotency store should dedup it.
        await publisher.PublishAsync(message, ct);
        await Task.Delay(TimeSpan.FromSeconds(3), ct);

        _handler.InvocationCount.Should().Be(1, "idempotency must deduplicate the second delivery");
    }
    [Fact]
    public async Task HandlerFailsOnce_OnlySuccessIsMarked_AndRetrySurvivesInBroker()
    {
        _handler!.FailuresRemaining = 1;
        var id = Guid.NewGuid();
        var payload = JsonSerializer.Serialize(new IdempotencyTestEvent(id, "retry"), IdempotencyJsonContext.Default.IdempotencyTestEvent);
        var message = new OutboxMessage(id, "integration.test.idempotency.v1", payload, null, null, null, DateTimeOffset.UtcNow);
        await _host!.Services.GetRequiredService<IIntegrationEventPublisher>().PublishAsync(message, TestContext.Current.CancellationToken);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        while (_handler.InvocationCount == 0 && DateTimeOffset.UtcNow < deadline)
            await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.Equal(1, _handler.InvocationCount);
        Assert.Equal(2, _handler.Attempts);
        var store = _host.Services.GetRequiredService<IProcessedMessageStore>();
        Assert.True(await store.IsProcessedAsync("test-consumer-idempotency", id, TestContext.Current.CancellationToken));
        var redis = _host.Services.GetRequiredService<IConnectionMultiplexer>().GetDatabase();
        Assert.Equal("1", (string?)await redis.StringGetAsync($"idemp:test-consumer-idempotency:{id}"));
        await _host.Services.GetRequiredService<IIntegrationEventPublisher>().PublishAsync(message, TestContext.Current.CancellationToken);
        await Task.Delay(300, TestContext.Current.CancellationToken);
        Assert.Equal(2, _handler.Attempts);
    }

    [Fact]
    public async Task BrokerRestartsWithUnacknowledgedMessage_RedeliversUntilHandlerSucceeds()
    {
        var ct = TestContext.Current.CancellationToken;
        _handler!.FailuresRemaining = 1;
        _handler.FirstAttemptGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var id = Guid.NewGuid();
        var payload = JsonSerializer.Serialize(new IdempotencyTestEvent(id, "restart"), IdempotencyJsonContext.Default.IdempotencyTestEvent);
        var message = new OutboxMessage(id, "integration.test.idempotency.v1", payload, null, null, null, DateTimeOffset.UtcNow);
        await _host!.Services.GetRequiredService<IIntegrationEventPublisher>().PublishAsync(message, ct);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        while (_handler.Attempts == 0 && DateTimeOffset.UtcNow < deadline) await Task.Delay(50, ct);
        Assert.Equal(1, _handler.Attempts);
        await _rabbitmq.Container.StopAsync(ct);
        _handler.FirstAttemptGate.SetResult();
        await _rabbitmq.Container.StartAsync(ct);
        Assert.Equal(new Uri(_rabbitmq.ConnectionString).Port, new Uri(_rabbitmq.Container.GetConnectionString()).Port);
        deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (_handler.InvocationCount == 0 && DateTimeOffset.UtcNow < deadline) await Task.Delay(100, ct);
        Assert.Equal(1, _handler.InvocationCount);
        Assert.Equal(2, _handler.Attempts);
        Assert.True(await _host.Services.GetRequiredService<IProcessedMessageStore>()
            .IsProcessedAsync("test-consumer-idempotency", id, ct));
    }

    [Fact]
    public async Task HandlerAlwaysFails_RetriesBoundedlyThenConfirmsDeadLetter_WithoutSuccessMark()
    {
        var ct = TestContext.Current.CancellationToken;
        _handler!.FailuresRemaining = 100;
        await _host!.Services.GetRequiredService<RabbitMqIntegrationEventSubscriber>().Ready.WaitAsync(TimeSpan.FromSeconds(20), ct);
        var id = Guid.NewGuid();
        var payload = JsonSerializer.Serialize(new IdempotencyTestEvent(id, "poison"), IdempotencyJsonContext.Default.IdempotencyTestEvent);
        await _host.Services.GetRequiredService<IIntegrationEventPublisher>().PublishAsync(
            new OutboxMessage(id, "integration.test.idempotency.v1", payload, null, null, null, DateTimeOffset.UtcNow), ct);
        var connection = await _host.Services.GetRequiredService<RabbitMqConnectionProvider>().GetConnectionAsync(ct);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: ct);
        BasicGetResult? dead = null;
        var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        while (dead is null && DateTimeOffset.UtcNow < deadline)
        {
            dead = await channel.BasicGetAsync("test-consumer-idempotency.dead-letter", autoAck: true, ct);
            if (dead is null) await Task.Delay(100, ct);
        }
        Assert.NotNull(dead);
        Assert.Equal(id.ToString(), dead.BasicProperties.MessageId);
        Assert.Equal(5, _handler.Attempts);
        Assert.Equal(0, _handler.InvocationCount);
        Assert.False(await _host.Services.GetRequiredService<IProcessedMessageStore>().IsProcessedAsync("test-consumer-idempotency", id, ct));
    }

}
