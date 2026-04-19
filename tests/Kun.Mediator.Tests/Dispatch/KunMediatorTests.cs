using FluentAssertions;
using Kun.Core.Cqrs;
using Kun.Mediator.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Kun.Mediator.Tests.Dispatch;

// ---------------------------------------------------------------------------
// Minimal in-test domain types
// ---------------------------------------------------------------------------

public sealed record Ping(string Message) : IRequest<string>;

public sealed class PingHandler : IRequestHandler<Ping, string>
{
    public ValueTask<string> Handle(Ping request, CancellationToken cancellationToken)
        => ValueTask.FromResult(request.Message.ToUpperInvariant());
}

public sealed record PingWithCt(string Message) : IRequest<string>;

public sealed record PingNotification(string Message) : INotification;

public sealed class PingNotificationHandlerA : INotificationHandler<PingNotification>
{
    private readonly List<string> _log;
    public PingNotificationHandlerA(List<string> log) => _log = log;
    public ValueTask Handle(PingNotification notification, CancellationToken cancellationToken)
    {
        _log.Add($"A:{notification.Message}");
        return ValueTask.CompletedTask;
    }
}

public sealed class PingNotificationHandlerB : INotificationHandler<PingNotification>
{
    private readonly List<string> _log;
    public PingNotificationHandlerB(List<string> log) => _log = log;
    public ValueTask Handle(PingNotification notification, CancellationToken cancellationToken)
    {
        _log.Add($"B:{notification.Message}");
        return ValueTask.CompletedTask;
    }
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

public sealed class KunMediatorTests
{
    [Fact]
    public async Task Send_RoutesToRegisteredHandler_ReturnsResponse()
    {
        var services = new ServiceCollection();
        services.AddKunMediator(b =>
            b.AddRequestHandler<Ping, string, PingHandler>());

        var sp = services.BuildServiceProvider();
        var sender = sp.GetRequiredService<ISender>();

        var result = await sender.Send(new Ping("hello"), TestContext.Current.CancellationToken);

        result.Should().Be("HELLO");
    }

    [Fact]
    public async Task Send_UnregisteredRequest_Throws()
    {
        var services = new ServiceCollection();
        services.AddKunMediator(_ => { }); // no handlers registered

        var sp = services.BuildServiceProvider();
        var sender = sp.GetRequiredService<ISender>();

        var act = async () => await sender.Send(new Ping("x"), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Ping*");
    }

    [Fact]
    public async Task Send_PassesCancellationTokenToHandler()
    {
        var receivedTokenBox = new CancellationToken[1];
        var services = new ServiceCollection();
        // Register the box so DI can inject it into CapturingPingWithCtHandler.
        services.AddSingleton(receivedTokenBox);
        services.AddKunMediator(b =>
            b.AddRequestHandler<PingWithCt, string, CapturingPingWithCtHandler>());

        var sp = services.BuildServiceProvider();
        var sender = sp.GetRequiredService<ISender>();

        var token = TestContext.Current.CancellationToken;

        await sender.Send(new PingWithCt("hi"), token);

        receivedTokenBox[0].Should().Be(token);
    }

    [Fact]
    public async Task Publish_NoHandlers_CompletesWithoutError()
    {
        var services = new ServiceCollection();
        services.AddKunMediator(_ => { });
        var sp = services.BuildServiceProvider();
        var publisher = sp.GetRequiredService<IPublisher>();

        var act = async () =>
            await publisher.Publish(new PingNotification("test"), TestContext.Current.CancellationToken);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Publish_MultipleHandlers_InvokesAllSequentially()
    {
        var log = new List<string>();
        var services = new ServiceCollection();
        services.AddSingleton(log);
        services.AddKunMediator(b => b
            .AddNotificationHandler<PingNotification, PingNotificationHandlerA>()
            .AddNotificationHandler<PingNotification, PingNotificationHandlerB>());

        var sp = services.BuildServiceProvider();
        var publisher = sp.GetRequiredService<IPublisher>();

        await publisher.Publish(new PingNotification("ping"), TestContext.Current.CancellationToken);

        log.Should().ContainInOrder("A:ping", "B:ping");
    }

    // Helper handler that captures the CancellationToken into a shared array.
    private sealed class CapturingPingWithCtHandler : IRequestHandler<PingWithCt, string>
    {
        private readonly CancellationToken[] _box;
        public CapturingPingWithCtHandler(CancellationToken[] box) => _box = box;
        public ValueTask<string> Handle(PingWithCt request, CancellationToken cancellationToken)
        {
            _box[0] = cancellationToken;
            return ValueTask.FromResult(request.Message);
        }
    }
}
