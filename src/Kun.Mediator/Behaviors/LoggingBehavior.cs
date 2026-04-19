using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Kun.Mediator.Behaviors;

/// <summary>
/// Pipeline behavior that logs the request name on entry and the elapsed time on exit.
/// Logs at <see cref="LogLevel.Debug"/> for normal completion and
/// <see cref="LogLevel.Warning"/> when the handler throws.
/// </summary>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
public sealed partial class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : Kun.Core.Cqrs.IRequest<TResponse>
{
    private readonly ILogger<LoggingBehavior<TRequest, TResponse>> _logger;

    /// <summary>Initialises a new <see cref="LoggingBehavior{TRequest,TResponse}"/>.</summary>
    /// <param name="logger">The logger instance.</param>
    public LoggingBehavior(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc/>
    public async ValueTask<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;
        LogDispatching(_logger, requestName);
        var sw = Stopwatch.StartNew();
        try
        {
            var response = await next().ConfigureAwait(false);
            sw.Stop();
            LogCompleted(_logger, requestName, sw.ElapsedMilliseconds);
            return response;
        }
        catch
        {
            sw.Stop();
            LogFaulted(_logger, requestName, sw.ElapsedMilliseconds);
            throw;
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Kun.Mediator dispatching {RequestName}")]
    private static partial void LogDispatching(ILogger logger, string requestName);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Kun.Mediator completed {RequestName} in {ElapsedMs}ms")]
    private static partial void LogCompleted(ILogger logger, string requestName, long elapsedMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Kun.Mediator {RequestName} threw after {ElapsedMs}ms")]
    private static partial void LogFaulted(ILogger logger, string requestName, long elapsedMs);
}
