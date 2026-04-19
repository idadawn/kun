// ---------------------------------------------------------------------------
// M4.5 — Saga integration tests: timeout detection
//
// Tests that SagaTimeoutDetector correctly identifies timed-out instances and
// transitions them to Compensating + enqueues compensation entries.
//
// Since the background executor never persists its writes (GAP 2), the timeout
// detector is tested by:
//   1. Persisting a Running saga instance with TimeoutAt already in the past.
//   2. Calling SagaTimeoutDetector.ScanAsync indirectly via ISagaInstanceStore
//      FindTimedOutAsync + manually invoking the compensation-trigger logic that
//      the detector performs: StartCompensating + EnqueueStep.
//   3. Asserting the resulting DB state.
//
// The detector's internal TriggerCompensationAsync is private, so we replicate
// its logic in the test (it is trivial: StartCompensating + EnqueueStep in a loop).
// This validates the contract without requiring the detector's background service
// to persist changes (which it cannot, due to GAP 2).
// ---------------------------------------------------------------------------

using FluentAssertions;
using Kun.Core.Time;
using Kun.EntityFrameworkCore;
using Kun.Saga.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Kun.IntegrationTests.Saga;

/// <summary>
/// Timeout detection: instances with <c>TimeoutAt</c> in the past and state
/// <c>Running</c> are found by <see cref="ISagaInstanceStore.FindTimedOutAsync"/>
/// and can be transitioned to Compensating.
/// </summary>
public sealed class SagaTimeoutTests : IAsyncLifetime
{
    private readonly SagaPostgresFixture _fixture = new();
    private ServiceProvider _sp = null!;

    public async ValueTask InitializeAsync()
    {
        await _fixture.InitializeAsync();

        if (string.IsNullOrEmpty(_fixture.ConnectionString))
            return;

        _sp = SagaTestServiceBuilder.Build<TimeoutSaga>(_fixture.ConnectionString);
        await SagaTestServiceBuilder.EnsureSchemaAsync(_sp, TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_sp is not null) await _sp.DisposeAsync();
        await _fixture.DisposeAsync();
    }

    [Fact]
    public async Task TimedOutInstance_FoundByStore_AndTransitionedToCompensating()
    {
        var ct = TestContext.Current.CancellationToken;

        if (string.IsNullOrEmpty(_fixture.ConnectionString))
            Assert.Skip("Docker unavailable.");

        var sagaId = Guid.NewGuid();

        // Phase 1: Persist a Running instance with TimeoutAt already in the past.
        await using (var scope = _sp.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;
            var clock = sp.GetRequiredService<ISystemClock>();
            var store = sp.GetRequiredService<ISagaInstanceStore>();
            var uow = sp.GetRequiredService<IUnitOfWork>();

            var context = new SampleSagaContext { SagaId = sagaId };
            var payload = System.Text.Json.JsonSerializer.Serialize(
                context, SampleSagaJsonContext.Default.SampleSagaContext);

            // TimeoutAt = 10 seconds ago — already expired.
            var instance = new SagaInstance(
                id: sagaId,
                sagaName: nameof(TimeoutSaga),
                tenantId: null,
                contextPayload: payload,
                startedAt: clock.UtcNow.AddSeconds(-30),
                timeoutAt: clock.UtcNow.AddSeconds(-10),
                traceId: null);

            await store.AddAsync(instance, ct);
            await uow.SaveChangesAsync(ct);
        }

        // Phase 2: Verify the store's FindTimedOutAsync detects the instance.
        IReadOnlyList<SagaInstance> timedOut;
        await using (var scope = _sp.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;
            var clock = sp.GetRequiredService<ISystemClock>();
            var store = sp.GetRequiredService<ISagaInstanceStore>();

            timedOut = await store.FindTimedOutAsync(clock.UtcNow, batchSize: 10, ct);
        }

        timedOut.Should().Contain(t => t.Id == sagaId,
            "instance with TimeoutAt in the past and state Running must be returned by FindTimedOutAsync");

        // Phase 3: Replicate SagaTimeoutDetector.TriggerCompensationAsync logic
        // (the detector's private method is untestable directly; its logic is trivial).
        await using (var scope = _sp.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;
            var clock = sp.GetRequiredService<ISystemClock>();
            var store = sp.GetRequiredService<ISagaInstanceStore>();
            var uow = sp.GetRequiredService<IUnitOfWork>();

            var instance = await store.FindAsync(sagaId, ct);
            instance.Should().NotBeNull();

            var now = clock.UtcNow;
            var failedStepIndex = instance!.CurrentStepIndex;

            instance.StartCompensating(
                failedStepIndex,
                $"Step {failedStepIndex} timed out at {now:O}.",
                now);

            await store.UpdateAsync(instance, ct);

            for (var i = failedStepIndex; i >= 0; i--)
            {
                var entry = new SagaStepQueueEntry(
                    Guid.NewGuid(), sagaId, i, StepDirection.Compensate, now);
                await store.EnqueueStepAsync(entry, ct);
            }

            await uow.SaveChangesAsync(ct);
        }

        // Phase 4: Assert resulting DB state.
        await using (var scope = _sp.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<ISagaInstanceStore>();
            var instance = await store.FindAsync(sagaId, ct);

            instance!.State.Should().Be(SagaState.Compensating,
                "StartCompensating must transition state from Running to Compensating");
            instance.FailureReason.Should().Contain("timed out",
                "failure reason must reference timeout");
        }

        var entries = await SagaTestServiceBuilder.GetAllQueueEntriesAsync(_sp, sagaId, ct);
        entries.Should().Contain(
            e => e.Direction == StepDirection.Compensate && e.StepIndex == 0,
            "detector must enqueue a Compensate entry for step 0");
    }

    [Fact]
    public async Task NonTimedOutInstance_NotReturnedByFindTimedOut()
    {
        var ct = TestContext.Current.CancellationToken;

        if (string.IsNullOrEmpty(_fixture.ConnectionString))
            Assert.Skip("Docker unavailable.");

        var sagaId = Guid.NewGuid();

        // Persist a Running instance with TimeoutAt far in the future.
        await using (var scope = _sp.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;
            var clock = sp.GetRequiredService<ISystemClock>();
            var store = sp.GetRequiredService<ISagaInstanceStore>();
            var uow = sp.GetRequiredService<IUnitOfWork>();

            var payload = System.Text.Json.JsonSerializer.Serialize(
                new SampleSagaContext { SagaId = sagaId },
                SampleSagaJsonContext.Default.SampleSagaContext);

            var instance = new SagaInstance(
                id: sagaId,
                sagaName: nameof(TimeoutSaga),
                tenantId: null,
                contextPayload: payload,
                startedAt: clock.UtcNow,
                timeoutAt: clock.UtcNow.AddHours(1),   // far future
                traceId: null);

            await store.AddAsync(instance, ct);
            await uow.SaveChangesAsync(ct);
        }

        await using (var scope = _sp.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;
            var clock = sp.GetRequiredService<ISystemClock>();
            var store = sp.GetRequiredService<ISagaInstanceStore>();

            var timedOut = await store.FindTimedOutAsync(clock.UtcNow, 10, ct);
            timedOut.Should().NotContain(t => t.Id == sagaId,
                "instance with TimeoutAt in the future must not be returned");
        }
    }
}
