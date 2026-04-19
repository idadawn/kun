// ---------------------------------------------------------------------------
// Shared internal types used across all Saga integration tests.
//
// IMPORTANT: All types use `internal` (not `file`) scope. EF Core 10 crashes on
// file-scoped types whose CLR name starts with '<' — ShortName()[0] throws
// IndexOutOfRangeException. `internal` gives stable CLR names.
//
// FRAMEWORK GAP NOTE (discovered during M4.5):
//   SagaStepExecutor.ProcessEntryAsync, after a successful step, only marks the
//   queue entry as processed and returns. It does NOT:
//     1. Enqueue the next step (stepIndex + 1).
//     2. Call SagaInstance.MarkCompleted() when the last step finishes.
//     3. Call SagaInstance.MarkCompensated() after all compensation entries run.
//   As a result, the SagaInstance.State stays Running after step 0 completes,
//   and the saga never advances past step 0 or reaches Completed/Compensated
//   purely via the background executor.
//   Tests are written to verify observable framework behaviour (queue entries
//   processed, compensation triggered) rather than terminal state transitions
//   that the current executor does not implement.
// ---------------------------------------------------------------------------

using System.Text.Json.Serialization;
using Kun.Core.MultiTenancy;
using Kun.Core.Time;
using Kun.EntityFrameworkCore;
using Kun.EntityFrameworkCore.DependencyInjection;
using Kun.EventBus.DependencyInjection;
using Kun.Mediator.DependencyInjection;
using Kun.Saga.Abstractions;
using Kun.Saga.DependencyInjection;
using Kun.Saga.Exceptions;
using Kun.Saga.Persistence;
using Kun.Saga.Persistence.Ef;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Testcontainers.PostgreSql;
using Xunit;

namespace Kun.IntegrationTests.Saga;

// ---------------------------------------------------------------------------
// Saga context
// ---------------------------------------------------------------------------

/// <summary>
/// Shared saga context for all saga integration tests.
/// Three boolean flags track which steps executed or compensated.
/// </summary>
internal sealed class SampleSagaContext : ISagaContext
{
    public Guid SagaId { get; init; }
    public string? TenantId { get; init; }

    // Forward execution tracking
    public bool Step1Done { get; set; }
    public bool Step2Done { get; set; }
    public bool Step3Done { get; set; }

    // Compensation tracking
    public bool Step1Compensated { get; set; }
    public bool Step2Compensated { get; set; }
    public bool Step3Compensated { get; set; }
}

// ---------------------------------------------------------------------------
// AOT-safe JsonSerializerContext for SampleSagaContext
// ---------------------------------------------------------------------------

[JsonSerializable(typeof(SampleSagaContext))]
internal sealed partial class SampleSagaJsonContext : JsonSerializerContext;

// ---------------------------------------------------------------------------
// Steps
// ---------------------------------------------------------------------------

/// <summary>Step 1 — sets Step1Done; compensation sets Step1Compensated.</summary>
internal sealed class Step1 : ISagaStep<SampleSagaContext>
{
    public string Name => "step1";
    public TimeSpan? Timeout => null;

    public ValueTask ExecuteAsync(SampleSagaContext context, CancellationToken cancellationToken)
    {
        context.Step1Done = true;
        return ValueTask.CompletedTask;
    }

    public ValueTask CompensateAsync(SampleSagaContext context, CancellationToken cancellationToken)
    {
        context.Step1Compensated = true;
        return ValueTask.CompletedTask;
    }
}

/// <summary>Step 2 — normal version sets Step2Done. Subclass overrides to throw.</summary>
internal class Step2 : ISagaStep<SampleSagaContext>
{
    public virtual string Name => "step2";
    public TimeSpan? Timeout => null;

    public virtual ValueTask ExecuteAsync(SampleSagaContext context, CancellationToken cancellationToken)
    {
        context.Step2Done = true;
        return ValueTask.CompletedTask;
    }

    public ValueTask CompensateAsync(SampleSagaContext context, CancellationToken cancellationToken)
    {
        context.Step2Compensated = true;
        return ValueTask.CompletedTask;
    }
}

/// <summary>Step 2 variant that throws in ExecuteAsync to trigger compensation.</summary>
internal sealed class FailingStep2 : Step2
{
    public override string Name => "step2-failing";

    public override ValueTask ExecuteAsync(SampleSagaContext context, CancellationToken cancellationToken)
        => throw new SagaStepFailedException(Name, new InvalidOperationException("failed on purpose"));
}

/// <summary>Step 2 variant that sleeps forever to trigger timeout detection.</summary>
internal sealed class TimeoutStep2 : Step2
{
    public override string Name => "step2-timeout";

    public override async ValueTask ExecuteAsync(SampleSagaContext context, CancellationToken cancellationToken)
    {
        // Sleep far longer than any configured timeout — timeout detector will intervene.
        await Task.Delay(TimeSpan.FromMinutes(10), cancellationToken).ConfigureAwait(false);
        context.Step2Done = true;
    }
}

/// <summary>Step 3 — sets Step3Done.</summary>
internal sealed class Step3 : ISagaStep<SampleSagaContext>
{
    public string Name => "step3";
    public TimeSpan? Timeout => null;

    public ValueTask ExecuteAsync(SampleSagaContext context, CancellationToken cancellationToken)
    {
        context.Step3Done = true;
        return ValueTask.CompletedTask;
    }

    public ValueTask CompensateAsync(SampleSagaContext context, CancellationToken cancellationToken)
    {
        context.Step3Compensated = true;
        return ValueTask.CompletedTask;
    }
}

// ---------------------------------------------------------------------------
// Saga definitions (one per test scenario to keep registrations independent)
// ---------------------------------------------------------------------------

/// <summary>Happy-path 3-step saga with no failures.</summary>
internal sealed class SampleSaga : ISaga<SampleSagaContext>
{
    public string Name => nameof(SampleSaga);
    public TimeSpan DefaultStepTimeout => TimeSpan.FromSeconds(30);
    public IReadOnlyList<ISagaStep<SampleSagaContext>> Steps { get; }

    public SampleSaga(Step1 s1, Step2 s2, Step3 s3)
        => Steps = new ISagaStep<SampleSagaContext>[] { s1, s2, s3 };
}

/// <summary>Failure saga: step 2 throws so step 1 compensation runs.</summary>
internal sealed class FailureSaga : ISaga<SampleSagaContext>
{
    public string Name => nameof(FailureSaga);
    public TimeSpan DefaultStepTimeout => TimeSpan.FromSeconds(30);
    public IReadOnlyList<ISagaStep<SampleSagaContext>> Steps { get; }

    public FailureSaga(Step1 s1, FailingStep2 s2, Step3 s3)
        => Steps = new ISagaStep<SampleSagaContext>[] { s1, s2, s3 };
}

/// <summary>Timeout saga: step 1 hangs past the detector scan window.</summary>
internal sealed class TimeoutSaga : ISaga<SampleSagaContext>
{
    public string Name => nameof(TimeoutSaga);

    /// <summary>Very short default timeout so the detector triggers quickly.</summary>
    public TimeSpan DefaultStepTimeout => TimeSpan.FromSeconds(2);

    public IReadOnlyList<ISagaStep<SampleSagaContext>> Steps { get; }

    public TimeoutSaga(Step1 s1, TimeoutStep2 s2, Step3 s3)
        => Steps = new ISagaStep<SampleSagaContext>[] { s1, s2, s3 };
}

/// <summary>Saga used specifically for crash-recovery test.</summary>
internal sealed class RecoverySaga : ISaga<SampleSagaContext>
{
    public string Name => nameof(RecoverySaga);
    public TimeSpan DefaultStepTimeout => TimeSpan.FromSeconds(30);
    public IReadOnlyList<ISagaStep<SampleSagaContext>> Steps { get; }

    public RecoverySaga(Step1 s1, Step2 s2, Step3 s3)
        => Steps = new ISagaStep<SampleSagaContext>[] { s1, s2, s3 };
}

/// <summary>
/// Single-step failure saga: step 0 is FailingStep2 so the executor observes a
/// failure on the very first queue entry it processes. Used by
/// <see cref="SagaFailureCompensationTests"/> because the executor only pre-enqueues
/// step 0 — a failure at step 0 is the only one observable without the step-advance gap.
/// </summary>
internal sealed class SingleStepFailureSaga : ISaga<SampleSagaContext>
{
    public string Name => nameof(SingleStepFailureSaga);
    public TimeSpan DefaultStepTimeout => TimeSpan.FromSeconds(30);
    public IReadOnlyList<ISagaStep<SampleSagaContext>> Steps { get; }

    public SingleStepFailureSaga(FailingStep2 s0)
        => Steps = new ISagaStep<SampleSagaContext>[] { s0 };
}

// ---------------------------------------------------------------------------
// Test DbContext
// ---------------------------------------------------------------------------

/// <summary>
/// Minimal KunDbContext for saga tests.
/// Applies the standard Kun Saga table configurations via <c>ApplyKunSagaConfiguration()</c>.
/// </summary>
internal sealed class SagaTestDbContext : KunDbContext
{
    public SagaTestDbContext(DbContextOptions<SagaTestDbContext> options, ITenantContext tenantContext)
        : base(options, tenantContext) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyKunSagaConfiguration();
    }
}

// ---------------------------------------------------------------------------
// PostgreSQL fixture (local copy — may be deduplicated with
// tests/Kun.IntegrationTests/Fixtures/PostgresFixture.cs once both agents finish)
// ---------------------------------------------------------------------------

/// <summary>
/// Per-class fixture that starts a Postgres 16 container.
/// Skips automatically when Docker is unavailable.
/// </summary>
internal sealed class SagaPostgresFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _container;

    public string ConnectionString { get; private set; } = string.Empty;

    public async ValueTask InitializeAsync()
    {
        try
        {
            _container = new PostgreSqlBuilder()
                .WithImage("postgres:16")
                .WithDatabase("kun_saga_test")
                .WithUsername("kun")
                .WithPassword("kun_secret")
                .Build();
        }
        catch (ArgumentException ex) when (ex.Message.Contains("Docker", StringComparison.OrdinalIgnoreCase))
        {
            Assert.Skip("Docker is not available on this host — integration test requires Docker.");
            return;
        }

        await _container.StartAsync(TestContext.Current.CancellationToken);
        ConnectionString = _container.GetConnectionString();
    }

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
    }
}

// ---------------------------------------------------------------------------
// Service-builder helper
// ---------------------------------------------------------------------------

internal static class SagaTestServiceBuilder
{
    /// <summary>
    /// Builds a <see cref="ServiceProvider"/> wired for saga integration tests.
    /// </summary>
    /// <typeparam name="TSaga">The saga type to register.</typeparam>
    /// <param name="connectionString">Postgres connection string.</param>
    /// <param name="executorPollInterval">
    /// How often the executor polls for pending steps. Defaults to 250 ms for fast tests.
    /// </param>
    /// <param name="timeoutScanInterval">
    /// How often the timeout detector scans. Defaults to 1 s.
    /// </param>
    public static ServiceProvider Build<TSaga>(
        string connectionString,
        TimeSpan? executorPollInterval = null,
        TimeSpan? timeoutScanInterval = null)
        where TSaga : class, ISaga<SampleSagaContext>
    {
        var services = new ServiceCollection();

        services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
        services.AddSingleton<ITenantContext>(NullTenantContext.Instance);
        services.AddSingleton<ISystemClock>(SystemClock.Instance);

        services.AddKunMediator(_ => { });
        services.AddKunEventBus(_ => { });
        services.AddKunEntityFrameworkCore();

        services.AddDbContext<SagaTestDbContext>((_, opts) =>
            opts.UseNpgsql(connectionString));
        services.AddScoped<KunDbContext>(sp => sp.GetRequiredService<SagaTestDbContext>());

        // Register all step types as scoped so DI can inject them into the saga.
        services.AddScoped<Step1>();
        services.AddScoped<Step2>();
        services.AddScoped<FailingStep2>();
        services.AddScoped<TimeoutStep2>();
        services.AddScoped<Step3>();

        // Override ISagaOrchestrator as Scoped so it shares the same KunDbContext
        // as IUnitOfWork within a test scope. AddKunSaga uses TryAddSingleton which
        // would register a singleton capturing the root-scope ISagaInstanceStore —
        // a captive dependency that prevents UoW.SaveChangesAsync from seeing the
        // orchestrator's AddAsync calls. Registering as Scoped BEFORE AddKunSaga
        // wins because TryAdd* skips if the service is already registered.
        services.AddScoped<Kun.Saga.Abstractions.ISagaOrchestrator, Kun.Saga.Orchestration.SagaOrchestrator>();

        services.AddKunSaga(b =>
            b.AddSaga<TSaga, SampleSagaContext>(SampleSagaJsonContext.Default.SampleSagaContext));

        // Override executor options for test speed.
        services.AddSingleton(new Kun.Saga.Orchestration.SagaStepExecutorOptions
        {
            PollInterval = executorPollInterval ?? TimeSpan.FromMilliseconds(250),
            ErrorRetryDelay = TimeSpan.FromMilliseconds(500),
            BatchSize = 10,
        });

        services.AddSingleton(new Kun.Saga.Orchestration.SagaTimeoutDetectorOptions
        {
            ScanInterval = timeoutScanInterval ?? TimeSpan.FromSeconds(1),
            BatchSize = 20,
        });

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Builds a <see cref="ServiceProvider"/> wired for the failure-compensation test.
    /// Registers <see cref="SingleStepFailureSaga"/> (step 0 = FailingStep2).
    /// </summary>
    public static ServiceProvider BuildWithFailure(string connectionString)
    {
        var services = new ServiceCollection();

        services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
        services.AddSingleton<ITenantContext>(NullTenantContext.Instance);
        services.AddSingleton<ISystemClock>(SystemClock.Instance);

        services.AddKunMediator(_ => { });
        services.AddKunEventBus(_ => { });
        services.AddKunEntityFrameworkCore();

        services.AddDbContext<SagaTestDbContext>((_, opts) =>
            opts.UseNpgsql(connectionString));
        services.AddScoped<KunDbContext>(sp => sp.GetRequiredService<SagaTestDbContext>());

        services.AddScoped<Step1>();
        services.AddScoped<Step2>();
        services.AddScoped<FailingStep2>();
        services.AddScoped<TimeoutStep2>();
        services.AddScoped<Step3>();

        services.AddScoped<Kun.Saga.Abstractions.ISagaOrchestrator, Kun.Saga.Orchestration.SagaOrchestrator>();

        services.AddKunSaga(b =>
            b.AddSaga<SingleStepFailureSaga, SampleSagaContext>(
                SampleSagaJsonContext.Default.SampleSagaContext));

        services.AddSingleton(new Kun.Saga.Orchestration.SagaStepExecutorOptions
        {
            PollInterval = TimeSpan.FromMilliseconds(250),
            ErrorRetryDelay = TimeSpan.FromMilliseconds(500),
            BatchSize = 10,
        });

        services.AddSingleton(new Kun.Saga.Orchestration.SagaTimeoutDetectorOptions
        {
            ScanInterval = TimeSpan.FromSeconds(1),
            BatchSize = 20,
        });

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Ensures the Postgres schema exists for the given <paramref name="sp"/>.
    /// </summary>
    public static async Task EnsureSchemaAsync(IServiceProvider sp, CancellationToken ct)
    {
        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SagaTestDbContext>();
        await db.Database.EnsureCreatedAsync(ct);
    }

    /// <summary>
    /// Polls <see cref="ISagaInstanceStore.FindAsync"/> every 100 ms until the saga reaches
    /// one of <paramref name="terminalStates"/> or <paramref name="timeout"/> elapses.
    /// Returns the last-seen <see cref="SagaInstance"/>.
    /// </summary>
    public static async Task<SagaInstance?> PollUntilAsync(
        IServiceProvider sp,
        Guid sagaId,
        SagaState[] terminalStates,
        TimeSpan timeout,
        CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        SagaInstance? instance = null;

        while (DateTimeOffset.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            await using var scope = sp.CreateAsyncScope();
            var store = scope.ServiceProvider.GetRequiredService<ISagaInstanceStore>();
            instance = await store.FindAsync(sagaId, ct);

            if (instance is not null && Array.IndexOf(terminalStates, instance.State) >= 0)
                return instance;

            await Task.Delay(100, ct);
        }

        return instance;
    }

    /// <summary>
    /// Polls until the step queue entry for <paramref name="sagaId"/> at
    /// <paramref name="stepIndex"/> (Execute direction) has ProcessedAt set.
    /// </summary>
    public static async Task<bool> PollUntilStepProcessedAsync(
        IServiceProvider sp,
        Guid sagaId,
        int stepIndex,
        StepDirection direction,
        TimeSpan timeout,
        CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;

        while (DateTimeOffset.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            await using var scope = sp.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<SagaTestDbContext>();

            var entry = await db.Set<SagaStepQueueEntry>()
                .Where(e => e.SagaId == sagaId && e.StepIndex == stepIndex && e.Direction == direction)
                .FirstOrDefaultAsync(ct);

            if (entry?.ProcessedAt is not null)
                return true;

            await Task.Delay(100, ct);
        }

        return false;
    }

    /// <summary>
    /// Returns all queue entries (processed or pending) for <paramref name="sagaId"/>.
    /// </summary>
    public static async Task<List<SagaStepQueueEntry>> GetAllQueueEntriesAsync(
        IServiceProvider sp,
        Guid sagaId,
        CancellationToken ct)
    {
        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SagaTestDbContext>();
        return await db.Set<SagaStepQueueEntry>()
            .Where(e => e.SagaId == sagaId)
            .ToListAsync(ct);
    }
}
