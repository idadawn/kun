using Kun.Core.Time;
using Kun.EntityFrameworkCore;
using Kun.Saga.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Kun.Saga.Orchestration;

/// <summary>
/// Background service that periodically scans for timed-out saga instances and triggers
/// compensation. An instance is considered timed-out when its <c>TimeoutAt</c> is in the past
/// and its state is <see cref="SagaState.Running"/>.
/// </summary>
public sealed partial class SagaTimeoutDetector : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SagaTimeoutDetector> _logger;
    private readonly SagaTimeoutDetectorOptions _options;

    /// <summary>Creates a new <see cref="SagaTimeoutDetector"/>.</summary>
    /// <param name="scopeFactory">Factory for creating DI scopes per scan cycle.</param>
    /// <param name="logger">Logger for timeout events.</param>
    /// <param name="options">Detector configuration options.</param>
    public SagaTimeoutDetector(
        IServiceScopeFactory scopeFactory,
        ILogger<SagaTimeoutDetector> logger,
        SagaTimeoutDetectorOptions options)
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
                await ScanAsync(stoppingToken).ConfigureAwait(false);
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
                await Task.Delay(_options.ScanInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        LogStopped(_logger);
    }

    private async Task ScanAsync(CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<ISagaInstanceStore>();
        var clock = scope.ServiceProvider.GetRequiredService<ISystemClock>();
        var dbContext = scope.ServiceProvider.GetRequiredService<KunDbContext>();

        var now = clock.UtcNow;
        var timedOut = await store.FindTimedOutAsync(now, _options.BatchSize, ct).ConfigureAwait(false);

        if (timedOut.Count == 0)
        {
            return;
        }

        LogTimedOutFound(_logger, timedOut.Count);

        foreach (var instance in timedOut)
        {
            try
            {
                await TriggerCompensationAsync(instance, store, clock, dbContext, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogCompensationTriggerFailed(_logger, instance.Id, ex);
            }
        }
    }

    private static async Task TriggerCompensationAsync(
        SagaInstance instance,
        ISagaInstanceStore store,
        ISystemClock clock,
        KunDbContext dbContext,
        CancellationToken ct)
    {
        var now = clock.UtcNow;
        var failedStepIndex = instance.CurrentStepIndex;

        instance.StartCompensating(
            failedStepIndex,
            $"Step {failedStepIndex} timed out at {now:O}.",
            now);

        await store.UpdateAsync(instance, ct).ConfigureAwait(false);

        // Enqueue only the timed-out step for compensation; each compensate step
        // enqueues the previous one on success (sequential chaining).
        var entry = new SagaStepQueueEntry(
            id: Guid.NewGuid(),
            sagaId: instance.Id,
            stepIndex: failedStepIndex,
            direction: StepDirection.Compensate,
            enqueuedAt: now);

        await store.EnqueueStepAsync(entry, ct).ConfigureAwait(false);

        // Commit the state transition and the compensation queue entry atomically.
        // Without this SaveChangesAsync the timeout detection is invisible to other
        // background services (SagaStepExecutor) which query the DB directly.
        await dbContext.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "SagaTimeoutDetector started.")]
    private static partial void LogStarted(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "SagaTimeoutDetector stopped.")]
    private static partial void LogStopped(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "SagaTimeoutDetector: found {Count} timed-out saga(s).")]
    private static partial void LogTimedOutFound(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "SagaTimeoutDetector: failed to trigger compensation for saga {SagaId}.")]
    private static partial void LogCompensationTriggerFailed(ILogger logger, Guid sagaId, Exception ex);

    [LoggerMessage(Level = LogLevel.Error, Message = "Unexpected error in SagaTimeoutDetector scan cycle.")]
    private static partial void LogUnexpectedError(ILogger logger, Exception ex);
}

/// <summary>Configuration options for <see cref="SagaTimeoutDetector"/>.</summary>
public sealed class SagaTimeoutDetectorOptions
{
    /// <summary>Gets or sets how often the detector scans for timed-out sagas. Defaults to 5 seconds.</summary>
    public TimeSpan ScanInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Gets or sets the maximum number of timed-out instances per scan batch. Defaults to 20.</summary>
    public int BatchSize { get; set; } = 20;
}
