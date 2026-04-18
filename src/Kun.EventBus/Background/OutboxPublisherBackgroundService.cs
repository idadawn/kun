using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kun.EventBus.Background;

/// <summary>
/// Hosted background service that polls the outbox store for pending messages and
/// publishes them via <see cref="IIntegrationEventPublisher"/>.
/// </summary>
public sealed partial class OutboxPublisherBackgroundService : BackgroundService
{
    /// <summary>The <see cref="ActivitySource"/> name used by all Kun.EventBus.Outbox activities.</summary>
    internal const string ActivitySourceName = "Kun.EventBus.Outbox";

    private static readonly ActivitySource s_activitySource = new(ActivitySourceName);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IIntegrationEventPublisher _publisher;
    private readonly OutboxPublisherOptions _options;
    private readonly ILogger<OutboxPublisherBackgroundService> _logger;
    private readonly TimeProvider _timeProvider;

    /// <summary>Initializes a new <see cref="OutboxPublisherBackgroundService"/>.</summary>
    public OutboxPublisherBackgroundService(
        IServiceScopeFactory scopeFactory,
        IIntegrationEventPublisher publisher,
        IOptions<OutboxPublisherOptions> options,
        ILogger<OutboxPublisherBackgroundService> logger,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(publisher);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _scopeFactory = scopeFactory;
        _publisher = publisher;
        _options = options.Value;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LogStarted(_logger);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessBatchAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                LogUnexpectedError(_logger, ex);
            }

            try
            {
                await Task.Delay(_options.PollInterval, _timeProvider, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        LogStopped(_logger);
    }

    internal async Task ProcessBatchAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IOutboxMessageStore>();

        var messages = await store.FetchPendingAsync(_options.BatchSize, cancellationToken).ConfigureAwait(false);

        foreach (var message in messages)
        {
            ActivityContext parentContext = default;
            if (message.TraceParent is not null)
            {
                ActivityContext.TryParse(message.TraceParent, message.TraceState, out parentContext);
            }

            using var activity = s_activitySource.StartActivity(
                "outbox.publish",
                ActivityKind.Producer,
                parentContext);

            activity?.SetTag("messaging.system", "outbox");
            activity?.SetTag("messaging.destination", message.EventTypeId);
            activity?.SetTag("messaging.message_id", message.Id.ToString());

            try
            {
                await _publisher.PublishAsync(message, cancellationToken).ConfigureAwait(false);
                message.MarkPublished(_timeProvider.GetUtcNow());
                LogPublished(_logger, message.Id, message.EventTypeId);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                message.IncrementAttempt();

                if (message.AttemptCount >= _options.MaxAttempts)
                {
                    message.MarkFailed(ex.Message);
                    LogFailed(_logger, message.Id, message.EventTypeId, message.AttemptCount);
                }
                else
                {
                    LogRetrying(_logger, message.Id, message.EventTypeId, message.AttemptCount, _options.MaxAttempts, ex);
                }
            }

            await store.UpdateAsync(message, cancellationToken).ConfigureAwait(false);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Kun.EventBus outbox publisher started.")]
    private static partial void LogStarted(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Kun.EventBus outbox publisher stopped.")]
    private static partial void LogStopped(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Outbox message {MessageId} ({EventTypeId}) published successfully.")]
    private static partial void LogPublished(ILogger logger, Guid messageId, string eventTypeId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Outbox message {MessageId} ({EventTypeId}) failed permanently after {AttemptCount} attempts.")]
    private static partial void LogFailed(ILogger logger, Guid messageId, string eventTypeId, int attemptCount);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Outbox message {MessageId} ({EventTypeId}) publish attempt {AttemptCount}/{MaxAttempts} failed; will retry.")]
    private static partial void LogRetrying(ILogger logger, Guid messageId, string eventTypeId, int attemptCount, int maxAttempts, Exception ex);

    [LoggerMessage(Level = LogLevel.Error, Message = "Unexpected error in outbox publisher loop.")]
    private static partial void LogUnexpectedError(ILogger logger, Exception ex);
}
