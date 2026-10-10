using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Kun.Core.MultiTenancy;
using Kun.EntityFrameworkCore;
using Kun.EntityFrameworkCore.DependencyInjection;
using Kun.EventBus.DependencyInjection;
using Kun.IntegrationTests.Fixtures;
using Kun.Mediator.DependencyInjection;
using Kun.Saga.Abstractions;
using Kun.Saga.DependencyInjection;
using Kun.Saga.Orchestration;
using Kun.Saga.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Kun.IntegrationTests.Saga;

public sealed class SagaExecutionBoundaryTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task TwoExecutors_SameSaga_ExecuteEachStepOnce()
    {
        var witness = new ExecutionWitness { PauseFirst = true };
        using var first = BuildHost(witness);
        using var second = BuildHost(witness);
        var id = await Seed(first);
        await Task.WhenAll(first.StartAsync(TestContext.Current.CancellationToken), second.StartAsync(TestContext.Current.CancellationToken));
        try
        {
            await witness.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            // 让另一副本有机会轮询并争抢同一个尚未完成的步骤。
            await Task.Delay(350, TestContext.Current.CancellationToken);
            witness.Release.TrySetResult();
            var instance = await Poll(first, id);
            instance.State.Should().Be(SagaState.Completed);
            foreach (var index in new[] { 0, 1, 2 })
                witness.Calls.Count(call => call.Index == index && !call.Compensate).Should().Be(1);
            witness.Calls.Select(call => call.Scope).Distinct().Should().HaveCount(3);
        }
        finally { witness.Release.TrySetResult(); await Task.WhenAll(first.StopAsync(CancellationToken.None), second.StopAsync(CancellationToken.None)); }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Executor_PersistsMutatedContext_IncludingPartialFailureAndReverseCompensation(int attempts)
    {
        var witness = new ExecutionWitness { FailStep = 1 };
        using var host = BuildHost(witness, maxAttempts: attempts);
        var id = await Seed(host);
        await host.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            var instance = await Poll(host, id);
            instance.State.Should().Be(SagaState.Compensated);
            var context = JsonSerializer.Deserialize(instance.ContextPayload, ExecutionJsonContext.Default.ExecutionContext)!;
            context.Executed.Should().Equal(Enumerable.Repeat(1, attempts).Prepend(0));
            context.Compensated.Should().Equal(1, 0);
            witness.Calls.Select(call => call.Scope).Distinct().Should().HaveCount(attempts + 3);
        }
        finally { await host.StopAsync(CancellationToken.None); }
    }

    [Fact]
    public async Task Retry_SurvivesRestart_UsesFreshScopeAndRollsBackFailedLocalWrites()
    {
        var witness = new ExecutionWitness { FailStep = 0, FailAttempts = 1, WriteLocalRow = true };
        using var first = BuildHost(witness, maxAttempts: 2);
        using var second = BuildHost(witness, maxAttempts: 2);
        var id = await Seed(first);
        await first.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            await WaitForRetry(first, id);
        }
        finally { await first.StopAsync(CancellationToken.None); }
        await using (var scope = first.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ExecutionDbContext>();
            (await db.Set<StepLocalWrite>().CountAsync(value => value.Id == id, TestContext.Current.CancellationToken)).Should().Be(0);
            var entry = await db.Set<SagaStepQueueEntry>().SingleAsync(value => value.SagaId == id, TestContext.Current.CancellationToken);
            entry.AttemptCount.Should().Be(1);
            entry.NextAttemptAt.Should().NotBeNull();
            entry.ProcessedAt.Should().BeNull();
        }
        await second.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            await Task.Delay(100, TestContext.Current.CancellationToken);
            witness.Calls.Should().HaveCount(1, "重启不能绕过已持久化的退避时间");
            (await Poll(second, id)).State.Should().Be(SagaState.Completed);
            witness.Calls.Count(call => call.Index == 0 && !call.Compensate).Should().Be(2);
            witness.Calls.Should().OnlyContain(call => !call.Compensate);
            witness.Calls.Select(call => call.Scope).Distinct().Should().HaveCount(4);
            await using var scope = second.Services.CreateAsyncScope();
            (await scope.ServiceProvider.GetRequiredService<ExecutionDbContext>().Set<StepLocalWrite>()
                .CountAsync(value => value.Id == id, TestContext.Current.CancellationToken)).Should().Be(1);
        }
        finally { await second.StopAsync(CancellationToken.None); }
    }

    [Fact]
    public async Task Shutdown_RollsBackPendingExecution_ThenAnotherWorkerResumes()
    {
        var witness = new ExecutionWitness { PauseFirst = true };
        using var first = BuildHost(witness);
        using var second = BuildHost(witness);
        var id = await Seed(first);
        await first.StartAsync(TestContext.Current.CancellationToken);
        try { await witness.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken); }
        finally { await first.StopAsync(CancellationToken.None); }
        await using (var scope = first.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ExecutionDbContext>();
            var entry = await db.Set<SagaStepQueueEntry>().SingleAsync(value => value.SagaId == id, TestContext.Current.CancellationToken);
            entry.ProcessedAt.Should().BeNull(); entry.AttemptCount.Should().Be(0);
            (await db.Set<SagaInstance>().SingleAsync(value => value.Id == id, TestContext.Current.CancellationToken)).State.Should().Be(SagaState.Running);
        }
        witness.Release.TrySetResult();
        await second.StartAsync(TestContext.Current.CancellationToken);
        try { (await Poll(second, id)).State.Should().Be(SagaState.Completed); witness.Calls.Should().OnlyContain(call => !call.Compensate); }
        finally { await second.StopAsync(CancellationToken.None); }
    }

    [Fact]
    public async Task Timeout_TwoDetectorsAndExecutors_ProduceOneCompensationChain()
    {
        var witness = new ExecutionWitness { PauseFirst = true, Timeout = TimeSpan.FromSeconds(1) };
        using var first = BuildHost(witness);
        using var second = BuildHost(witness);
        var id = await Seed(first);
        await Task.WhenAll(first.StartAsync(TestContext.Current.CancellationToken), second.StartAsync(TestContext.Current.CancellationToken));
        try
        {
            (await Poll(first, id)).State.Should().Be(SagaState.Compensated);
            witness.Calls.Count(call => !call.Compensate).Should().Be(1);
            witness.Calls.Count(call => call.Compensate).Should().Be(1);
            await using var scope = first.Services.CreateAsyncScope();
            var entries = await scope.ServiceProvider.GetRequiredService<ExecutionDbContext>().Set<SagaStepQueueEntry>()
                .Where(value => value.SagaId == id).ToListAsync(TestContext.Current.CancellationToken);
            entries.Should().HaveCount(2).And.OnlyContain(value => value.ProcessedAt != null);
        }
        finally { witness.Release.TrySetResult(); await Task.WhenAll(first.StopAsync(CancellationToken.None), second.StopAsync(CancellationToken.None)); }
    }

    private static async Task WaitForRetry(IHost host, Guid id)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        while (true)
        {
            await using var scope = host.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ExecutionDbContext>();
            if (await db.Set<SagaStepQueueEntry>().AnyAsync(value => value.SagaId == id && value.NextAttemptAt != null, timeout.Token)) return;
            await Task.Delay(20, timeout.Token);
        }
    }

    private IHost BuildHost(ExecutionWitness witness, int maxAttempts = 1) => Host.CreateDefaultBuilder()
        .UseDefaultServiceProvider(options => { options.ValidateScopes = true; options.ValidateOnBuild = true; })
        .ConfigureLogging(logging => logging.ClearProviders())
        .ConfigureServices(services =>
        {
            services.AddSingleton(witness);
            services.AddSingleton<ITenantContext>(NullTenantContext.Instance);
            services.AddKunMediator(_ => { }); services.AddKunEventBus(_ => { }); services.AddKunEntityFrameworkCore();
            services.AddDbContext<ExecutionDbContext>(options => options.UseNpgsql(postgres.ConnectionString));
            services.AddScoped<KunDbContext>(sp => sp.GetRequiredService<ExecutionDbContext>());
            services.AddScoped<ExecutionScope>();
            services.AddKunSaga(builder => builder.AddSaga<ExecutionSaga, ExecutionContext>(ExecutionJsonContext.Default.ExecutionContext));
            services.AddSingleton(new SagaStepExecutorOptions { PollInterval = TimeSpan.FromMilliseconds(20), ErrorRetryDelay = TimeSpan.FromMilliseconds(20), MaxAttempts = maxAttempts, RetryBaseDelay = TimeSpan.FromSeconds(3) });
            services.AddSingleton(new SagaTimeoutDetectorOptions { ScanInterval = TimeSpan.FromMilliseconds(30) });
        }).Build();

    private static async Task<Guid> Seed(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ExecutionDbContext>();
        await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        var context = new ExecutionContext();
        await scope.ServiceProvider.GetRequiredService<ISagaOrchestrator>().StartAsync<ExecutionSaga, ExecutionContext>(context, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return context.SagaId;
    }

    private static async Task<SagaInstance> Poll(IHost host, Guid id)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(12));
        while (true)
        {
            await using var scope = host.Services.CreateAsyncScope();
            var instance = await scope.ServiceProvider.GetRequiredService<ISagaInstanceStore>().FindAsync(id, deadline.Token);
            if (instance?.State is SagaState.Completed or SagaState.Compensated or SagaState.Failed) return instance;
            await Task.Delay(25, deadline.Token);
        }
    }
}

internal sealed class ExecutionWitness
{
    internal bool PauseFirst { get; init; }
    internal int FailStep { get; init; } = -1;
    internal int FailAttempts { get; init; } = int.MaxValue;
    internal bool WriteLocalRow { get; init; }
    internal TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(10);
    internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal ConcurrentQueue<(int Index, bool Compensate, Guid Scope)> Calls { get; } = [];
}
internal sealed class ExecutionScope(ExecutionDbContext db) { internal Guid Id { get; } = Guid.NewGuid(); internal ExecutionDbContext Db => db; }
internal sealed class ExecutionSaga(ExecutionWitness witness, ExecutionScope scope) : ISaga<ExecutionContext>
{
    public string Name => "execution.probe.v1";
    public TimeSpan DefaultStepTimeout => witness.Timeout;
    public IReadOnlyList<ISagaStep<ExecutionContext>> Steps { get; } = [new ExecutionStep(witness, scope, 0), new ExecutionStep(witness, scope, 1), new ExecutionStep(witness, scope, 2)];
}
internal sealed class ExecutionStep(ExecutionWitness witness, ExecutionScope scope, int index) : ISagaStep<ExecutionContext>
{
    public string Name => "step-" + index;
    public TimeSpan? Timeout => null;
    public async ValueTask ExecuteAsync(ExecutionContext context, CancellationToken cancellationToken)
    {
        witness.Calls.Enqueue((index, false, scope.Id));
        context.Executed.Add(index);
        if (index == 0 && witness.PauseFirst) { witness.Entered.TrySetResult(); await witness.Release.Task.WaitAsync(cancellationToken); }
        if (index == 0 && witness.WriteLocalRow)
        {
            scope.Db.Add(new StepLocalWrite { Id = context.SagaId });
            await scope.Db.SaveChangesAsync(cancellationToken);
        }
        if (index == witness.FailStep && witness.Calls.Count(call => call.Index == index && !call.Compensate) <= witness.FailAttempts)
            throw new InvalidOperationException("Intentional partial step failure");
    }
    public ValueTask CompensateAsync(ExecutionContext context, CancellationToken cancellationToken)
    {
        witness.Calls.Enqueue((index, true, scope.Id)); context.Compensated.Add(index); return ValueTask.CompletedTask;
    }
}
internal sealed class ExecutionContext : ISagaContext
{
    public Guid SagaId { get; init; } = Guid.NewGuid();
    public string? TenantId { get; init; }
    public List<int> Executed { get; init; } = [];
    public List<int> Compensated { get; init; } = [];
}
[JsonSerializable(typeof(ExecutionContext))]
internal sealed partial class ExecutionJsonContext : JsonSerializerContext;

internal sealed class StepLocalWrite { public Guid Id { get; set; } }
internal sealed class ExecutionDbContext(DbContextOptions<ExecutionDbContext> options, ITenantContext tenant) : KunDbContext(options, tenant)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        Kun.Saga.Persistence.Ef.KunSagaDbContextExtensions.ApplyKunSagaConfiguration(modelBuilder);
        modelBuilder.Entity<StepLocalWrite>().ToTable("step_local_writes").HasKey(value => value.Id);
    }
}
