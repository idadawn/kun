using FluentAssertions;
using Kun.Core.Cqrs;
using Kun.Mediator.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Kun.Mediator.Tests.Pipeline;

// ---------------------------------------------------------------------------
// Shared domain types for pipeline tests
// ---------------------------------------------------------------------------

public sealed record EchoRequest(string Value) : IRequest<string>;

public sealed class EchoHandler : IRequestHandler<EchoRequest, string>
{
    private readonly List<string> _log;
    public EchoHandler(List<string> log) => _log = log;

    public ValueTask<string> Handle(EchoRequest request, CancellationToken cancellationToken)
    {
        _log.Add("handler");
        return ValueTask.FromResult(request.Value);
    }
}

/// <summary>
/// Spy behavior that appends "before:{name}" before calling next and "after:{name}" after.
/// </summary>
public class SpyBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly List<string> _log;
    private readonly string _name;

    public SpyBehavior(List<string> log, string name)
    {
        _log = log;
        _name = name;
    }

    public async ValueTask<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        _log.Add($"before:{_name}");
        var result = await next().ConfigureAwait(false);
        _log.Add($"after:{_name}");
        return result;
    }
}

// Concrete closed types so DI can register them as named transient services.
public sealed class SpyBehaviorA : SpyBehavior<EchoRequest, string>
{
    public SpyBehaviorA(List<string> log) : base(log, "A") { }
}

public sealed class SpyBehaviorB : SpyBehavior<EchoRequest, string>
{
    public SpyBehaviorB(List<string> log) : base(log, "B") { }
}

public sealed class SpyBehaviorC : SpyBehavior<EchoRequest, string>
{
    public SpyBehaviorC(List<string> log) : base(log, "C") { }
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

public sealed class PipelineBehaviorTests
{
    [Fact]
    public async Task Behaviors_RunInOnionOrder()
    {
        // Register A, B, C — expect: before:A, before:B, before:C, handler, after:C, after:B, after:A
        var log = new List<string>();
        var services = new ServiceCollection();
        services.AddSingleton(log);
        services.AddKunMediator(b => b
            .AddPipelineBehavior<EchoRequest, string, SpyBehaviorA>()
            .AddPipelineBehavior<EchoRequest, string, SpyBehaviorB>()
            .AddPipelineBehavior<EchoRequest, string, SpyBehaviorC>()
            .AddRequestHandler<EchoRequest, string, EchoHandler>());

        var sp = services.BuildServiceProvider();
        var sender = sp.GetRequiredService<ISender>();

        await sender.Send(new EchoRequest("x"), TestContext.Current.CancellationToken);

        log.Should().ContainInOrder(
            "before:A", "before:B", "before:C",
            "handler",
            "after:C", "after:B", "after:A");
    }

    [Fact]
    public async Task Behaviors_CancellationPropagatesToHandler()
    {
        var log = new List<string>();
        var receivedCt = new CancellationToken[1];

        var services = new ServiceCollection();
        services.AddSingleton(log);
        services.AddSingleton(receivedCt);

        services.AddKunMediator(b => b
            .AddPipelineBehavior<EchoRequest, string, SpyBehaviorA>()
            .AddRequestHandler<EchoRequest, string, CapturingEchoHandler>());

        var sp = services.BuildServiceProvider();
        var sender = sp.GetRequiredService<ISender>();

        var token = TestContext.Current.CancellationToken;

        await sender.Send(new EchoRequest("ct-test"), token);

        receivedCt[0].Should().Be(token);
    }

    [Fact]
    public async Task SingleBehavior_WrapsHandlerCorrectly()
    {
        var log = new List<string>();
        var services = new ServiceCollection();
        services.AddSingleton(log);
        services.AddKunMediator(b => b
            .AddPipelineBehavior<EchoRequest, string, SpyBehaviorA>()
            .AddRequestHandler<EchoRequest, string, EchoHandler>());

        var sp = services.BuildServiceProvider();
        var sender = sp.GetRequiredService<ISender>();

        var result = await sender.Send(new EchoRequest("wrapped"), TestContext.Current.CancellationToken);

        result.Should().Be("wrapped");
        log.Should().ContainInOrder("before:A", "handler", "after:A");
    }

    private sealed class CapturingEchoHandler : IRequestHandler<EchoRequest, string>
    {
        private readonly CancellationToken[] _box;
        public CapturingEchoHandler(CancellationToken[] box) => _box = box;

        public ValueTask<string> Handle(EchoRequest request, CancellationToken cancellationToken)
        {
            _box[0] = cancellationToken;
            return ValueTask.FromResult(request.Value);
        }
    }
}
