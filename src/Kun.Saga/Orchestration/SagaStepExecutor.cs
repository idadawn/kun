using System.Diagnostics;
using Kun.Core.Time;
using Kun.Saga.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Kun.Saga.Orchestration;

/// <summary>
/// Background service that polls <see cref="ISagaInstanceStore.FetchPendingStepsAsync"/> and
/// drives saga step execution and compensation. Creates an <see cref="ActivitySource"/> span
/// for each step to enable distributed tracing.
/// </summary>
public sealed partial class SagaStepExecutor : BackgroundService
{
    internal const string ActivitySourceName = "Kun.Saga";

    private static readonly ActivitySource s_activitySource = new(ActivitySourceName);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SagaStepExecutor> _logger;
    private readonly SagaStepExecutorOptions _options;

    /// <summary>Creates a new <see cref="SagaStepExecutor"/>.</summary>
    /// <param name="scopeFactory">Factory for creating DI scopes per poll cycle.</param>
    /// <param name="logger">Logger for step execution events.</param>
    /// <param name="options">Executor configuration options.</param>
    public SagaStepExecutor(
        IServiceScopeFactory scopeFactory,
        ILogger<SagaStepExecutor> logger,
        SagaStepExecutorOptions options)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(options);
        _scopeFactory = scopeFactory;
        _logger = logger;
        _options = options;
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LogStarted(_logger);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var processed = await ProcessBatchAsync(stoppingToken).ConfigureAwait(false);
                if (processed == 0)
                {
                    await Task.Delay(_options.PollInterval, stoppingToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                LogUnexpectedError(_logger, ex);
                await Task.Delay(_options.ErrorRetryDelay, stoppingToken).ConfigureAwait(false);
            }
        }

        LogStopped(_logger);
    }

    private async Task<int> ProcessBatchAsync(CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<ISagaInstanceStore>();
        var registry = scope.ServiceProvider.GetRequiredService<SagaRegistry>();
        var clock = scope.ServiceProvider.GetRequiredService<ISystemClock>();

        var entries = await store.FetchPendingStepsAsync(_options.BatchSize, ct).ConfigureAwait(false);

        foreach (var entry in entries)
        {
            await ProcessEntryAsync(entry, store, registry, clock, scope.ServiceProvider, ct).ConfigureAwait(false);
        }

        return entries.Count;
    }

    private async Task ProcessEntryAsync(
        SagaStepQueueEntry entry,
        ISagaInstanceStore store,
        SagaRegistry registry,
        ISystemClock clock,
        IServiceProvider serviceProvider,
        CancellationToken ct)
    {
        var spanName = entry.Direction == StepDirection.Execute
            ? "saga.step.execute"
            : "saga.step.compensate";

        using var activity = s_activitySource.StartActivity(spanName, ActivityKind.Internal);
        activity?.SetTag("saga.step_index", entry.StepIndex);
        activity?.SetTag("saga.saga_id", entry.SagaId.ToString());
        activity?.SetTag("saga.direction", entry.Direction.ToString());

        entry.IncrementAttempt();

        var instance = await store.FindAsync(entry.SagaId, ct).ConfigureAwait(false);
        if (instance is null)
        {
            LogInstanceNotFound(_logger, entry.SagaId, entry.Id);
            entry.MarkProcessed(clock.UtcNow);
            await store.UpdateStepAsync(entry, ct).ConfigureAwait(false);
            return;
        }

        activity?.SetTag("saga.name", instance.SagaName);

        var registration = registry.Find(instance.SagaName);
        if (registration is null)
        {
            LogRegistrationNotFound(_logger, instance.SagaName, instance.Id);
            instance.Fail($"Saga type '{instance.SagaName}' not registered.", clock.UtcNow);
            await store.UpdateAsync(instance, ct).ConfigureAwait(false);
            entry.MarkProcessed(clock.UtcNow);
            await store.UpdateStepAsync(entry, ct).ConfigureAwait(false);
            return;
        }

        try
        {
            await registration.ExecuteStepAsync(
                entry.StepIndex,
                entry.Direction,
                instance.ContextPayload,
                serviceProvider,
                ct).ConfigureAwait(false);

            entry.MarkProcessed(clock.UtcNow);
            await store.UpdateStepAsync(entry, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogStepFailed(_logger, entry.StepIndex, entry.Direction, entry.SagaId, ex);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);

            if (entry.Direction == StepDirection.Execute)
            {
                instance.StartCompensating(entry.StepIndex, ex.Message, clock.UtcNow);
                await store.UpdateAsync(instance, ct).ConfigureAwait(false);

                for (var i = entry.StepIndex; i >= 0; i--)
                {
                    var compensateEntry = new SagaStepQueueEntry(
                        id: Guid.NewGuid(),
                        sagaId: instance.Id,
                        stepIndex: i,
                        direction: StepDirection.Compensate,
                        enqueuedAt: clock.UtcNow);
                    await store.EnqueueStepAsync(compensateEntry, ct).ConfigureAwait(false);
                }
            }
            else
            {
                instance.Fail($"Compensation step {entry.StepIndex} failed: {ex.Message}", clock.UtcNow);
                await store.UpdateAsync(instance, ct).ConfigureAwait(false);
            }

            entry.MarkProcessed(clock.UtcNow);
            await store.UpdateStepAsync(entry, ct).ConfigureAwait(false);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "SagaStepExecutor started.")]
    private static partial void LogStarted(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "SagaStepExecutor stopped.")]
    private static partial void LogStopped(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "SagaStepExecutor: saga instance {SagaId} not found for step entry {EntryId}. Skipping.")]
    private static partial void LogInstanceNotFound(ILogger logger, Guid sagaId, Guid entryId);

    [LoggerMessage(Level = LogLevel.Error, Message = "SagaStepExecutor: saga '{SagaName}' is not registered. Marking saga {SagaId} as Failed.")]
    private static partial void LogRegistrationNotFound(ILogger logger, string sagaName, Guid sagaId);

    [LoggerMessage(Level = LogLevel.Error, Message = "SagaStepExecutor: step {StepIndex} ({Direction}) for saga {SagaId} failed.")]
    private static partial void LogStepFailed(ILogger logger, int stepIndex, StepDirection direction, Guid sagaId, Exception ex);

    [LoggerMessage(Level = LogLevel.Error, Message = "Unexpected error in SagaStepExecutor poll cycle.")]
    private static partial void LogUnexpectedError(ILogger logger, Exception ex);
}

/// <summary>Configuration options for <see cref="SagaStepExecutor"/>.</summary>
public sealed class SagaStepExecutorOptions
{
    /// <summary>Gets or sets the delay between polls when the step queue is empty. Defaults to 2 seconds.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>Gets or sets the delay before retrying after an unhandled error. Defaults to 5 seconds.</summary>
    public TimeSpan ErrorRetryDelay { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Gets or sets the maximum number of step entries processed per poll cycle. Defaults to 10.</summary>
    public int BatchSize { get; set; } = 10;
}
