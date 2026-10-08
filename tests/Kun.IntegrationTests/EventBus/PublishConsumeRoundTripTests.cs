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
// Internal types — JSON source generator requires non-file-scoped visibility
// ---------------------------------------------------------------------------

internal sealed record RoundTripEventA(Guid Id, string Name);

internal sealed class RoundTripEventAHandler : IIntegrationEventHandler<RoundTripEventA>
{
    private readonly TaskCompletionSource<RoundTripEventA> _tcs;

    public RoundTripEventAHandler(TaskCompletionSource<RoundTripEventA> tcs) => _tcs = tcs;

    public ValueTask HandleAsync(RoundTripEventA @event, MessageContext context, CancellationToken cancellationToken)
    {
        _tcs.TrySetResult(@event);
        return ValueTask.CompletedTask;
    }
}

[JsonSerializable(typeof(RoundTripEventA))]
internal sealed partial class RoundTripJsonContext : JsonSerializerContext { }

// ---------------------------------------------------------------------------
// Test class
// ---------------------------------------------------------------------------

public sealed class PublishConsumeRoundTripTests : IAsyncLifetime
{
    private readonly RabbitMqFixture _rabbitmq = new();
    private IHost? _host;
    private readonly TaskCompletionSource<RoundTripEventA> _received = new();

    public async ValueTask InitializeAsync()
    {
        await _rabbitmq.InitializeAsync();

        if (string.IsNullOrEmpty(_rabbitmq.ConnectionString))
        {
            Assert.Skip("Docker not available.");
            return;
        }

        var received = _received;

        _host = Host.CreateDefaultBuilder()
            .ConfigureLogging(l => l.SetMinimumLevel(LogLevel.Warning))
            .ConfigureServices(services =>
            {
                // Register IOptions<RabbitMqOptions> directly via object-initializer
                // because ConnectionString and ConsumerName are required init-only properties.
                services.AddSingleton<IOptions<RabbitMqOptions>>(
                    Options.Create(new RabbitMqOptions
                    {
                        ConnectionString = _rabbitmq.ConnectionString,
                        ConsumerName = "test-consumer-roundtrip",
                    }));

                services.AddKunEventBus(b =>
                {
                    b.Register<RoundTripEventA>("integration.test.event-a.v1");
                    b.UseInMemoryIdempotency();
                });

                services.AddRabbitMq(
                    // Options already registered above; pass no-op to satisfy the signature.
                    _ => { },
                    builder => builder.Subscribe<RoundTripEventA, RoundTripEventAHandler>(
                        "integration.test.event-a.v1",
                        RoundTripJsonContext.Default.RoundTripEventA));

                services.AddSingleton(received);
                services.AddScoped<RoundTripEventAHandler>();
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
    public async Task Publish_ShouldDeliverEventToHandler_WithinTimeout()
    {
        var ct = TestContext.Current.CancellationToken;

        if (_host is null)
        {
            Assert.Skip("Docker not available.");
            return;
        }

        var eventId = Guid.NewGuid();
        var payload = JsonSerializer.Serialize(
            new RoundTripEventA(eventId, "hello"),
            RoundTripJsonContext.Default.RoundTripEventA);

        var publisher = _host.Services.GetRequiredService<IIntegrationEventPublisher>();
        await publisher.PublishAsync(new OutboxMessage(
            id: eventId,
            eventTypeId: "integration.test.event-a.v1",
            payload: payload,
            tenantId: null,
            traceParent: null,
            traceState: null,
            occurredAt: DateTimeOffset.UtcNow), ct);

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        var received = await _received.Task.WaitAsync(linked.Token);

        received.Id.Should().Be(eventId);
        received.Name.Should().Be("hello");
    }
}
