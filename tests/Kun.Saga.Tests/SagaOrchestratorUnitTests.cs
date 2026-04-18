using FluentAssertions;
using Kun.Core.Time;
using Kun.Saga.Abstractions;
using Kun.Saga.Orchestration;
using Kun.Saga.Persistence;
using NSubstitute;
using Xunit;

namespace Kun.Saga.Tests;

// ---------------------------------------------------------------------------
// Minimal in-test domain types
// ---------------------------------------------------------------------------

public sealed class TestSagaContext : ISagaContext
{
    public Guid SagaId { get; init; } = Guid.NewGuid();
    public string? TenantId { get; init; }
    public string OrderId { get; init; } = string.Empty;
}

public sealed class NoOpStep : ISagaStep<TestSagaContext>
{
    public string Name => "NoOpStep";
    public TimeSpan? Timeout => null;
    public ValueTask ExecuteAsync(TestSagaContext context, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    public ValueTask CompensateAsync(TestSagaContext context, CancellationToken cancellationToken) => ValueTask.CompletedTask;
}

public sealed class TestSaga : ISaga<TestSagaContext>
{
    public string Name => "TestSaga";
    public TimeSpan DefaultStepTimeout => TimeSpan.FromMinutes(5);
    public IReadOnlyList<ISagaStep<TestSagaContext>> Steps { get; } = [new NoOpStep()];
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

public sealed class SagaOrchestratorUnitTests
{
    private static (SagaOrchestrator Orchestrator, ISagaInstanceStore Store, SagaRegistry Registry, ISystemClock Clock)
        BuildOrchestrator(string sagaName = "TestSaga")
    {
        var store = Substitute.For<ISagaInstanceStore>();
        var clock = Substitute.For<ISystemClock>();
        clock.UtcNow.Returns(DateTimeOffset.UtcNow);

        // Build a minimal registry with a start delegate that calls the store.
        Task<Guid> StartDelegate(object ctx, ISagaInstanceStore s, ISystemClock c, CancellationToken ct)
        {
            var context = (TestSagaContext)ctx;
            var now = c.UtcNow;
            var instance = new SagaInstance(
                id: context.SagaId,
                sagaName: sagaName,
                tenantId: context.TenantId,
                contextPayload: "{}",
                startedAt: now,
                timeoutAt: now.AddMinutes(5),
                traceId: null);
            var entry = new SagaStepQueueEntry(
                id: Guid.NewGuid(),
                sagaId: context.SagaId,
                stepIndex: 0,
                direction: StepDirection.Execute,
                enqueuedAt: now);
            s.AddAsync(instance, ct);
            s.EnqueueStepAsync(entry, ct);
            return Task.FromResult(context.SagaId);
        }

        static Task ExecuteStepDelegate(int _, StepDirection __, string ___, IServiceProvider ____, CancellationToken _____) => Task.CompletedTask;

        var registration = new SagaRegistration(sagaName, StartDelegate, ExecuteStepDelegate);
        var registry = new SagaRegistry([registration]);

        var orchestrator = new SagaOrchestrator(store, registry, clock);
        return (orchestrator, store, registry, clock);
    }

    [Fact]
    public async Task StartAsync_CreatesInstanceWithRunningState()
    {
        var (orchestrator, store, _, _) = BuildOrchestrator();
        var context = new TestSagaContext { OrderId = "ORD-001" };

        var sagaId = await orchestrator.StartAsync<TestSaga, TestSagaContext>(context, TestContext.Current.CancellationToken);

        sagaId.Should().Be(context.SagaId);
        await store.Received(1).AddAsync(
            Arg.Is<SagaInstance>(i =>
                i.Id == context.SagaId &&
                i.SagaName == "TestSaga" &&
                i.State == SagaState.Running &&
                i.CurrentStepIndex == 0),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartAsync_EnqueuesFirstStepAsExecute()
    {
        var (orchestrator, store, _, _) = BuildOrchestrator();
        var context = new TestSagaContext { OrderId = "ORD-002" };

        await orchestrator.StartAsync<TestSaga, TestSagaContext>(context, TestContext.Current.CancellationToken);

        await store.Received(1).EnqueueStepAsync(
            Arg.Is<SagaStepQueueEntry>(e =>
                e.SagaId == context.SagaId &&
                e.StepIndex == 0 &&
                e.Direction == StepDirection.Execute &&
                e.ProcessedAt == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartAsync_UnregisteredSaga_Throws()
    {
        var store = Substitute.For<ISagaInstanceStore>();
        var clock = Substitute.For<ISystemClock>();
        clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        var registry = new SagaRegistry([]); // empty registry

        var orchestrator = new SagaOrchestrator(store, registry, clock);
        var context = new TestSagaContext();

        var act = async () => await orchestrator.StartAsync<TestSaga, TestSagaContext>(context, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*TestSaga*");
    }

    [Fact]
    public async Task StartAsync_ReturnsSagaIdMatchingContext()
    {
        var (orchestrator, _, _, _) = BuildOrchestrator();
        var expectedId = Guid.NewGuid();
        var context = new TestSagaContext { SagaId = expectedId };

        var result = await orchestrator.StartAsync<TestSaga, TestSagaContext>(context, TestContext.Current.CancellationToken);

        result.Should().Be(expectedId);
    }
}
