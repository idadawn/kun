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
// Internal types — two distinct versions of the same logical event
// ---------------------------------------------------------------------------

internal sealed record TestEventBV1(Guid Id, string NameV1);
internal sealed record TestEventBV2(Guid Id, string NameV2, int Version);

internal sealed class TestEventBV1Handler : IIntegrationEventHandler<TestEventBV1>
{
    private readonly TaskCompletionSource<TestEventBV1> _tcs;
    public TestEventBV1Handler(TaskCompletionSource<TestEventBV1> tcs) => _tcs = tcs;

    public ValueTask HandleAsync(TestEventBV1 @event, MessageContext context, CancellationToken cancellationToken)
    {
        _tcs.TrySetResult(@event);
        return ValueTask.CompletedTask;
    }
}

internal sealed class TestEventBV2Handler : IIntegrationEventHandler<TestEventBV2>
{
    private readonly TaskCompletionSource<TestEventBV2> _tcs;
    public TestEventBV2Handler(TaskCompletionSource<TestEventBV2> tcs) => _tcs = tcs;

    public ValueTask HandleAsync(TestEventBV2 @event, MessageContext context, CancellationToken cancellationToken)
    {
        _tcs.TrySetResult(@event);
        return ValueTask.CompletedTask;
    }
}

[JsonSerializable(typeof(TestEventBV1))]
[JsonSerializable(typeof(TestEventBV2))]
internal sealed partial class VersioningJsonContext : JsonSerializerContext { }

// ---------------------------------------------------------------------------
// Test class
// ---------------------------------------------------------------------------

public sealed class EventVersioningTests : IAsyncLifetime
{
    private readonly RabbitMqFixture _rabbitmq = new();
    private IHost? _host;
    private readonly TaskCompletionSource<TestEventBV1> _v1Received = new();
    private readonly TaskCompletionSource<TestEventBV2> _v2Received = new();

    public async ValueTask InitializeAsync()
    {
        await _rabbitmq.InitializeAsync();

        if (string.IsNullOrEmpty(_rabbitmq.ConnectionString))
        {
            Assert.Skip("Docker not available.");
            return;
        }

        var v1Tcs = _v1Received;
        var v2Tcs = _v2Received;

        _host = Host.CreateDefaultBuilder()
            .ConfigureLogging(l => l.SetMinimumLevel(LogLevel.Warning))
            .ConfigureServices(services =>
            {
                services.AddKunEventBus(b =>
                {
                    b.Register<TestEventBV1>("integration.test.event-b.v1");
                    b.Register<TestEventBV2>("integration.test.event-b.v2");
                    b.UseInMemoryIdempotency();
                });

                services.AddRabbitMq(
                    _ => { },
                    builder =>
                    {
                        builder.Subscribe<TestEventBV1, TestEventBV1Handler>(
                            "integration.test.event-b.v1",
                            VersioningJsonContext.Default.TestEventBV1);
                        builder.Subscribe<TestEventBV2, TestEventBV2Handler>(
                            "integration.test.event-b.v2",
                            VersioningJsonContext.Default.TestEventBV2);
                    });

                services.AddSingleton<IOptions<RabbitMqOptions>>(
                    Options.Create(new RabbitMqOptions
                    {
                        ConnectionString = _rabbitmq.ConnectionString,
                        ConsumerName = "test-consumer-versioning",
                    }));

                services.AddSingleton(v1Tcs);
                services.AddSingleton(v2Tcs);
                services.AddScoped<TestEventBV1Handler>();
                services.AddScoped<TestEventBV2Handler>();
            })
            .Build();

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
    }

    [Fact]
    public async Task V1Message_RoutedToV1Handler_V2Message_RoutedToV2Handler_WithCrossVersionIsolation()
    {
        var ct = TestContext.Current.CancellationToken;

        if (_host is null)
        {
            Assert.Skip("Docker not available.");
            return;
        }

        var publisher = _host.Services.GetRequiredService<IIntegrationEventPublisher>();

        var v1Id = Guid.NewGuid();
        var v2Id = Guid.NewGuid();

        var v1Payload = JsonSerializer.Serialize(
            new TestEventBV1(v1Id, "v1-name"),
            VersioningJsonContext.Default.TestEventBV1);

        await publisher.PublishAsync(new OutboxMessage(
            id: v1Id,
            eventTypeId: "integration.test.event-b.v1",
            payload: v1Payload,
            tenantId: null,
            traceParent: null,
            traceState: null,
            occurredAt: DateTimeOffset.UtcNow), ct);

        var v2Payload = JsonSerializer.Serialize(
            new TestEventBV2(v2Id, "v2-name", 2),
            VersioningJsonContext.Default.TestEventBV2);

        await publisher.PublishAsync(new OutboxMessage(
            id: v2Id,
            eventTypeId: "integration.test.event-b.v2",
            payload: v2Payload,
            tenantId: null,
            traceParent: null,
            traceState: null,
            occurredAt: DateTimeOffset.UtcNow), ct);

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        var v1 = await _v1Received.Task.WaitAsync(linked.Token);
        var v2 = await _v2Received.Task.WaitAsync(linked.Token);

        v1.Id.Should().Be(v1Id, "v1 handler must receive the v1 message");
        v1.NameV1.Should().Be("v1-name");

        v2.Id.Should().Be(v2Id, "v2 handler must receive the v2 message");
        v2.NameV2.Should().Be("v2-name");
        v2.Version.Should().Be(2);
    }
}
