using Kun.Core.Cqrs;
using Kun.Mediator.Behaviors;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Kun.Mediator.Tests;

public sealed class PerformanceOptionsTests
{
    private sealed record Request : IRequest<int>;
    [Fact]
    public async Task ConfiguredThreshold_ControlsWarning_WithoutChangingResult()
    {
        var logger = new Capture<PerformanceBehavior<Request, int>>();
        var behavior = new PerformanceBehavior<Request, int>(logger, Options.Create(new PerformanceBehaviorOptions { ThresholdMs = 0 }));
        var result = await behavior.Handle(new Request(), async () =>
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
            return 42;
        }, TestContext.Current.CancellationToken);
        Assert.Equal(42, result);
        Assert.Contains(logger.Messages, message => message.Contains("threshold 0ms", StringComparison.Ordinal));
    }
    private sealed class Capture<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }
}
