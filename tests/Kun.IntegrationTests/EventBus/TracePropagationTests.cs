using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Kun.EventBus;
using Kun.EventBus.DependencyInjection;
using Kun.EventBus.Outbox;
using Kun.EventBus.RabbitMQ.DependencyInjection;
using Kun.EventBus.RabbitMQ.Options;
using Kun.IntegrationTests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Kun.IntegrationTests.EventBus;

// ---------------------------------------------------------------------------
// Internal types — source generator requires non-file-scoped visibility
// ---------------------------------------------------------------------------

internal sealed record TraceTestEvent(Guid Id);

internal sealed class TraceCapturingHandler : IIntegrationEventHandler<TraceTestEvent>
{
    private readonly TaskCompletionSource<ActivityTraceId?> _tcs;

    public TraceCapturingHandler(TaskCompletionSource<ActivityTraceId?> tcs) => _tcs = tcs;

    public ValueTask HandleAsync(TraceTestEvent @event, MessageContext context, CancellationToken cancellationToken)
    {
        _tcs.TrySetResult(Activity.Current?.TraceId);
        return ValueTask.CompletedTask;
    }
}

[JsonSerializable(typeof(TraceTestEvent))]
internal sealed partial class TraceJsonContext : JsonSerializerContext { }

// ---------------------------------------------------------------------------
// Test class
// ---------------------------------------------------------------------------

public sealed class TracePropagationTests : IAsyncLifetime
{
    private readonly RabbitMqFixture _rabbitmq = new();
    private IHost? _host;
    private readonly TaskCompletionSource<ActivityTraceId?> _receivedTraceId = new();

    public async ValueTask InitializeAsync()
    {
        await _rabbitmq.InitializeAsync();

        if (string.IsNullOrEmpty(_rabbitmq.ConnectionString))
        {
            Assert.Skip("Docker not available.");
            return;
        }

        var tcs = _receivedTraceId;

        _host = Host.CreateDefaultBuilder()
            .ConfigureLogging(l => l.SetMinimumLevel(LogLevel.Warning))
            .ConfigureServices(services =>
            {
                services.AddKunEventBus(b =>
                {
                    b.Register<TraceTestEvent>("integration.test.trace.v1");
                    b.UseInMemoryIdempotency();
                });

                // AddRabbitMq registers the subscriber/publisher; pass no-op options lambda.
                // IOptions<RabbitMqOptions> is registered after with a direct object initializer
                // because ConnectionString and ConsumerName are required init-only properties.
                services.AddRabbitMq(
                    _ => { },
                    builder => builder.Subscribe<TraceTestEvent, TraceCapturingHandler>(
                        "integration.test.trace.v1",
                        TraceJsonContext.Default.TraceTestEvent));

                services.AddSingleton<IOptions<RabbitMqOptions>>(
                    Options.Create(new RabbitMqOptions
                    {
                        ConnectionString = _rabbitmq.ConnectionString,
                        ConsumerName = "test-consumer-trace",
                    }));

                services.AddSingleton(tcs);
                services.AddScoped<TraceCapturingHandler>();
            })
            .Build();

        await _host.StartAsync(TestContext.Current.CancellationToken);
        await _host.Services.GetRequiredService<Kun.EventBus.RabbitMQ.Consuming.RabbitMqIntegrationEventSubscriber>()
            .Ready.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_host is not null)
        {
            await _host.StopAsync(CancellationToken.None);
            _host.Dispose();
        }

        await _rabbitmq.DisposeAsync();
    }

    [Fact]
    public async Task ProducerTraceId_PropagatesTo_ConsumerHandler()
    {
        var ct = TestContext.Current.CancellationToken;

        if (_host is null)
        {
            Assert.Skip("Docker not available.");
            return;
        }

        // 本测试拥有自己的 listener，不能依赖其他并行测试临时注册的全局监听器。
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "RabbitMqIntegrationEventSubscriber",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(listener);

        // Build a W3C traceparent manually so we control the TraceId regardless of whether
        // an ActivityListener is wired up (in CI no listener may be active).
        var producerTraceId = ActivityTraceId.CreateRandom();
        var spanId = ActivitySpanId.CreateRandom();
        var traceParent = $"00-{producerTraceId}-{spanId}-01";

        var eventId = Guid.NewGuid();
        var payload = JsonSerializer.Serialize(
            new TraceTestEvent(eventId),
            TraceJsonContext.Default.TraceTestEvent);

        var publisher = _host.Services.GetRequiredService<IIntegrationEventPublisher>();
        await publisher.PublishAsync(new OutboxMessage(
            id: eventId,
            eventTypeId: "integration.test.trace.v1",
            payload: payload,
            tenantId: null,
            traceParent: traceParent,
            traceState: null,
            occurredAt: DateTimeOffset.UtcNow), ct);

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        var consumerTraceId = await _receivedTraceId.Task.WaitAsync(linked.Token);

        consumerTraceId.Should().NotBeNull("consumer must have an active Activity restored from traceparent");
        consumerTraceId!.Value.Should().Be(producerTraceId,
            "W3C traceparent header must propagate the producer TraceId to the consumer Activity");
    }
}
