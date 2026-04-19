using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Kun.Mediator.Behaviors;

/// <summary>
/// Pipeline behavior that logs a warning when the handler pipeline exceeds a duration
/// threshold. The threshold is currently hardcoded to 500 ms; a configurable version
/// backed by <c>IOptions&lt;PerformanceBehaviorOptions&gt;</c> is a planned follow-up.
/// </summary>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
public sealed partial class PerformanceBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : Kun.Core.Cqrs.IRequest<TResponse>
{
    /// <summary>
    /// Default threshold in milliseconds. Requests that take longer than this will be
    /// logged as warnings. A configurable alternative is a planned follow-up.
    /// </summary>
    public const long DefaultThresholdMs = 500;

    private readonly ILogger<PerformanceBehavior<TRequest, TResponse>> _logger;

    /// <summary>
    /// Initialises a new <see cref="PerformanceBehavior{TRequest,TResponse}"/>.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    public PerformanceBehavior(ILogger<PerformanceBehavior<TRequest, TResponse>> logger)
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
        var sw = Stopwatch.StartNew();
        var response = await next().ConfigureAwait(false);
        sw.Stop();

        if (sw.ElapsedMilliseconds > DefaultThresholdMs)
        {
            LogSlowRequest(_logger, typeof(TRequest).Name, sw.ElapsedMilliseconds, DefaultThresholdMs);
        }

        return response;
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Kun.Mediator slow request: {RequestName} took {ElapsedMs}ms (threshold {ThresholdMs}ms)")]
    private static partial void LogSlowRequest(
        ILogger logger, string requestName, long elapsedMs, long thresholdMs);
}
