using System.Diagnostics;
using Kun.Core.MultiTenancy;
using Kun.Core.Time;
using Kun.EntityFrameworkCore;
using Kun.Saga.Persistence;
using Kun.Saga.Persistence.Ef;
using Microsoft.EntityFrameworkCore;
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
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxAttempts);
        ArgumentOutOfRangeException.ThrowIfNegative(options.RetryBaseDelay.Ticks);
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
        var entries = await store.FetchPendingStepsAsync(_options.BatchSize, ct).ConfigureAwait(false);
        var processed = 0;
        foreach (var candidate in entries)
        {
            await using var entryScope = _scopeFactory.CreateAsyncScope();
            if (await ProcessEntryAsync(candidate, entryScope.ServiceProvider, ct).ConfigureAwait(false)) processed++;
        }
        return processed;
    }

    private async Task<bool> ProcessEntryAsync(SagaStepQueueEntry candidate, IServiceProvider services, CancellationToken ct)
    {
        var db = services.GetRequiredService<KunDbContext>();
        var clock = services.GetRequiredService<ISystemClock>();
        var store = services.GetRequiredService<ISagaInstanceStore>();
        await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        var instance = await SagaRowLock.TryAcquireAsync(db, candidate.SagaId, ct).ConfigureAwait(false);
        if (instance is null && await db.Set<SagaInstance>().AnyAsync(value => value.Id == candidate.SagaId, ct).ConfigureAwait(false)) return false;
        // 轮询结果不是租约。拿到实例行锁后重读队列，避免第二副本执行陈旧快照。
        var entry = await db.Set<SagaStepQueueEntry>().SingleOrDefaultAsync(value => value.Id == candidate.Id, ct).ConfigureAwait(false);
        if (entry is null || entry.ProcessedAt is not null || entry.NextAttemptAt > clock.UtcNow) return false;
        var tenant = services.GetService<ITenantContext>();
        if (instance is not null && tenant?.IsMultiTenant == true && !TenantIdValidation.IsValid(instance.TenantId))
            throw new InvalidOperationException("A multi-tenant saga requires a valid tenant identifier.");
        using var tenantScope = instance is not null && tenant?.IsMultiTenant == true ? tenant.BeginScope(instance.TenantId) : null;
        if (instance is null)
        {
            LogInstanceNotFound(_logger, entry.SagaId, entry.Id);
            entry.MarkProcessed(clock.UtcNow);
        }
        else if (instance.CurrentStepIndex != entry.StepIndex ||
            (entry.Direction == StepDirection.Execute ? instance.State != SagaState.Running : instance.State != SagaState.Compensating))
        {
            // 过期执行/补偿条目仅退休，不能再次触发副作用或倒退状态。
            entry.MarkProcessed(clock.UtcNow);
        }
        else
        {
            // Name/Steps 及其依赖只在恢复租户后的当前作用域内构造。
            var registration = services.GetRequiredService<SagaRegistry>().Find(instance.SagaName);
            if (registration is null)
            {
                LogRegistrationNotFound(_logger, instance.SagaName, instance.Id);
                instance.Fail($"Saga type '{instance.SagaName}' not registered.", clock.UtcNow);
                entry.MarkProcessed(clock.UtcNow);
            }
            else
            {
                using var activity = s_activitySource.StartActivity(entry.Direction == StepDirection.Execute ? "saga.step.execute" : "saga.step.compensate", ActivityKind.Internal);
                activity?.SetTag("saga.step_index", entry.StepIndex);
                activity?.SetTag("saga.saga_id", entry.SagaId.ToString());
                activity?.SetTag("saga.direction", entry.Direction.ToString());
                activity?.SetTag("saga.name", instance.SagaName);
                using var stepCancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
                var deadline = entry.Direction == StepDirection.Execute && instance.TimeoutAt is not null
                    ? instance.TimeoutAt.Value : clock.UtcNow + (registration.GetStepTimeout(entry.StepIndex, services) ?? TimeSpan.FromMinutes(5));
                var remaining = deadline - clock.UtcNow;
                await transaction.CreateSavepointAsync("kun_saga_step", ct).ConfigureAwait(false);
                SagaStepOutcome outcome;
                if (remaining <= TimeSpan.Zero) outcome = new(instance.ContextPayload, new TimeoutException("Saga step timed out before execution."));
                else
                {
                    stepCancellation.CancelAfter(remaining);
                    outcome = await registration.ExecuteAndCaptureAsync(entry.StepIndex, entry.Direction, instance.ContextPayload, services, stepCancellation.Token).ConfigureAwait(false);
                    if (outcome.Error is null && stepCancellation.IsCancellationRequested)
                        outcome = outcome with { Error = new TimeoutException("Saga step exceeded its timeout.") };
                }
                // 宿主关闭回滚本轮，不把关闭误判为业务失败或补偿。
                ct.ThrowIfCancellationRequested();
                if (outcome.Error is not null)
                {
                    // 失败步骤可能调用过 SaveChanges；撤销其本地数据，保留部分外部上下文供重试/补偿。
                    await transaction.RollbackToSavepointAsync("kun_saga_step", ct).ConfigureAwait(false);
                    db.ChangeTracker.Clear();
                    instance = (await store.FindAsync(entry.SagaId, ct).ConfigureAwait(false))!;
                    entry = await db.Set<SagaStepQueueEntry>().SingleAsync(value => value.Id == candidate.Id, ct).ConfigureAwait(false);
                    activity?.SetStatus(ActivityStatusCode.Error);
                    LogStepFailed(_logger, entry.StepIndex, entry.Direction, entry.SagaId, outcome.Error);
                }
                instance.UpdateContext(outcome.ContextPayload, clock.UtcNow);
                entry.IncrementAttempt();
                if (outcome.Error is not null && entry.AttemptCount < _options.MaxAttempts && !stepCancellation.IsCancellationRequested && clock.UtcNow < deadline)
                {
                    var delay = TimeSpan.FromSeconds(Math.Min(60, _options.RetryBaseDelay.TotalSeconds * Math.Pow(2, Math.Min(entry.AttemptCount - 1, 20))));
                    entry.ScheduleRetry(clock.UtcNow + delay);
                }
                else
                {
                    await AdvanceAsync(entry, instance, registration, outcome.Error, store, services, clock.UtcNow, ct).ConfigureAwait(false);
                    entry.MarkProcessed(clock.UtcNow);
                }
            }
        }
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return true;
    }

    private static async Task AdvanceAsync(SagaStepQueueEntry entry, SagaInstance instance, SagaRegistration registration, Exception? error,
        ISagaInstanceStore store, IServiceProvider services, DateTimeOffset now, CancellationToken ct)
    {
        if (error is not null)
        {
            if (entry.Direction == StepDirection.Compensate)
                instance.Fail($"Compensation step {entry.StepIndex} failed: {error.Message}", now);
            else
            {
                instance.StartCompensating(entry.StepIndex, error.Message, now);
                await store.EnqueueStepAsync(new SagaStepQueueEntry(Guid.NewGuid(), instance.Id, entry.StepIndex, StepDirection.Compensate, now), ct).ConfigureAwait(false);
            }
        }
        else if (entry.Direction == StepDirection.Execute)
        {
            var next = entry.StepIndex + 1;
            if (next >= registration.StepCount) instance.MarkCompleted(now);
            else
            {
                instance.MoveToStep(next, now, now + (registration.GetStepTimeout(next, services) ?? TimeSpan.FromMinutes(5)), instance.ContextPayload);
                await store.EnqueueStepAsync(new SagaStepQueueEntry(Guid.NewGuid(), instance.Id, next, StepDirection.Execute, now), ct).ConfigureAwait(false);
            }
        }
        else
        {
            var previous = entry.StepIndex - 1;
            if (previous < 0) instance.MarkCompensated(now);
            else
            {
                instance.MoveCompensationToStep(previous, now);
                await store.EnqueueStepAsync(new SagaStepQueueEntry(Guid.NewGuid(), instance.Id, previous, StepDirection.Compensate, now), ct).ConfigureAwait(false);
            }
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "SagaStepExecutor: step {StepIndex} for saga {SagaId} was superseded (saga state is now {State}). Skipping advance.")]
    private static partial void LogStepSuperseded(ILogger logger, int stepIndex, Guid sagaId, string state);

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

    /// <summary>Maximum attempts per execute/compensate entry. Defaults to one for compatibility; retries require idempotent steps.</summary>
    public int MaxAttempts { get; set; } = 1;

    /// <summary>Initial persisted exponential retry delay, capped at one minute. Defaults to one second.</summary>
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Gets or sets the maximum number of step entries processed per poll cycle. Defaults to 10.</summary>
    public int BatchSize { get; set; } = 10;
}
