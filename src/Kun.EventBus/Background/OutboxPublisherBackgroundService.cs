using System.Diagnostics;
using Kun.Core.MultiTenancy;
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
    private readonly IOutboxLockProvider _lockProvider;
    private readonly OutboxPublisherOptions _options;
    private readonly ILogger<OutboxPublisherBackgroundService> _logger;
    private readonly TimeProvider _timeProvider;

    /// <summary>Initializes a new <see cref="OutboxPublisherBackgroundService"/>.</summary>
    public OutboxPublisherBackgroundService(
        IServiceScopeFactory scopeFactory,
        IIntegrationEventPublisher publisher,
        IOutboxLockProvider lockProvider,
        IOptions<OutboxPublisherOptions> options,
        ILogger<OutboxPublisherBackgroundService> logger,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(publisher);
        ArgumentNullException.ThrowIfNull(lockProvider);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _scopeFactory = scopeFactory;
        _publisher = publisher;
        _lockProvider = lockProvider;
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
        var transactional = store as IOutboxBatchStore;
        var lockTtl = _options.PollInterval + _options.PollInterval;
        await using var lease = transactional is null
            ? await _lockProvider.TryAcquireAsync("outbox-publisher", lockTtl, cancellationToken).ConfigureAwait(false)
            : null;
        if (transactional is null && lease is null)
        {
            LogLockSkipped(_logger);
            return;
        }

        var messages = await store.FetchPendingAsync(_options.BatchSize, cancellationToken).ConfigureAwait(false);

        foreach (var message in messages)
        {
            if (message.NextAttemptAt > _timeProvider.GetUtcNow()) continue;
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
                var tenantContext = scope.ServiceProvider.GetService<ITenantContext>();
                if (tenantContext?.IsMultiTenant == true && !TenantIdValidation.IsValid(message.TenantId))
                {
                    throw new InvalidOperationException("A multi-tenant outbox message requires a valid tenant identifier.");
                }
                using var tenantScope = tenantContext?.IsMultiTenant == true
                    ? tenantContext.BeginScope(message.TenantId) : null;
                await _publisher.PublishAsync(message, cancellationToken).ConfigureAwait(false);
                message.MarkPublished(_timeProvider.GetUtcNow());
                LogPublished(_logger, message.Id, message.EventTypeId);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Clean shutdown: the stopping token fired before or during publish.
                // Do NOT increment AttemptCount — no broker interaction occurred.
                // Break out so the outer loop can exit cleanly.
                break;
            }
            catch (Exception ex)
            {
                // Any other exception (including broker connection failures that happen
                // to coincide with a shutdown) counts as a failed attempt. This covers
                // BrokerUnreachableException, socket errors, and channel-level failures.
                message.IncrementAttempt();

                if (message.AttemptCount >= _options.MaxAttempts)
                {
                    message.MarkFailed(ex.Message);
                    LogFailed(_logger, message.Id, message.EventTypeId, message.AttemptCount);
                }
                else
                {
                    var delay = TimeSpan.FromMilliseconds(Math.Clamp(_options.RetryBaseDelay.TotalMilliseconds *
                        Math.Pow(2, Math.Min(message.AttemptCount - 1, 16)), 0, 60_000));
                    message.ScheduleRetry(_timeProvider.GetUtcNow() + delay, ex.Message);
                    LogRetrying(_logger, message.Id, message.EventTypeId, message.AttemptCount, _options.MaxAttempts, ex);
                }
            }

            await store.UpdateAsync(message, cancellationToken).ConfigureAwait(false);
        }
        if (transactional is not null) await transactional.CompleteBatchAsync(cancellationToken).ConfigureAwait(false);
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

    [LoggerMessage(Level = LogLevel.Debug, Message = "Outbox publisher skipped batch — lock held by another instance.")]
    private static partial void LogLockSkipped(ILogger logger);
}
