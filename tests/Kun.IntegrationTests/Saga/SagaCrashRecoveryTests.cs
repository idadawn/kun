// ---------------------------------------------------------------------------
// M4.5 — Saga integration tests: crash recovery
//
// Proves the database is the source of truth for saga state. A saga is started
// (instance + step-0 queue entry written to DB) by a first ServiceProvider that
// is then disposed without running the executor — simulating a process crash.
//
// A second, fresh ServiceProvider using the same DB connection then reads the
// orphaned queue entry, confirming that pending work survives process restarts.
//
// Note: we do NOT start the background executor in the second host because
// GAP 2 (executor never calls SaveChangesAsync) means the executor cannot
// persist its processing results. Instead, we manually invoke the step-execution
// delegate and save changes, proving the pending entry is readable and executable
// by the fresh ServiceProvider.
// ---------------------------------------------------------------------------

using FluentAssertions;
using Kun.Core.Time;
using Kun.EntityFrameworkCore;
using Kun.Saga;
using Kun.Saga.Abstractions;
using Kun.Saga.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Kun.IntegrationTests.Saga;

/// <summary>
/// Crash recovery: pending queue entries written by <c>StartAsync</c> survive
/// process restart and can be picked up by a fresh service provider.
/// </summary>
public sealed class SagaCrashRecoveryTests : IAsyncLifetime
{
    private readonly SagaPostgresFixture _fixture = new();

    public async ValueTask InitializeAsync() => await _fixture.InitializeAsync();

    public async ValueTask DisposeAsync() => await _fixture.DisposeAsync();

    [Fact]
    public async Task PendingStepEntry_SurvivedServiceProviderDisposal_IsPickedUpByFreshSp()
    {
        var ct = TestContext.Current.CancellationToken;

        if (string.IsNullOrEmpty(_fixture.ConnectionString))
            Assert.Skip("Docker unavailable.");

        var sagaId = Guid.NewGuid();

        // -----------------------------------------------------------------------
        // Phase 1: "First process" — start saga, persist, dispose without executor.
        // -----------------------------------------------------------------------
        await using (var sp1 = SagaTestServiceBuilder.Build<RecoverySaga>(_fixture.ConnectionString))
        {
            await SagaTestServiceBuilder.EnsureSchemaAsync(sp1, ct);

            await using var scope = sp1.CreateAsyncScope();
            var orchestrator = scope.ServiceProvider.GetRequiredService<ISagaOrchestrator>();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            await orchestrator.StartAsync<RecoverySaga, SampleSagaContext>(
                new SampleSagaContext { SagaId = sagaId }, ct);
            await uow.SaveChangesAsync(ct);
        }
        // sp1 disposed — executor never started (simulates crash).

        // -----------------------------------------------------------------------
        // Phase 2: "Second process" — fresh SP, same DB. Reads orphaned entries.
        // -----------------------------------------------------------------------
        await using var sp2 = SagaTestServiceBuilder.Build<RecoverySaga>(_fixture.ConnectionString);

        // Verify the pending step-0 entry is visible to the fresh SP.
        await using (var scope = sp2.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<ISagaInstanceStore>();
            var pending = await store.FetchPendingStepsAsync(10, ct);

            pending.Should().Contain(
                e => e.SagaId == sagaId && e.StepIndex == 0 && e.Direction == StepDirection.Execute,
                "pending step-0 entry written by the first SP must survive and be visible to the fresh SP");
        }

        // Verify the saga instance is also visible.
        await using (var scope = sp2.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<ISagaInstanceStore>();
            var instance = await store.FindAsync(sagaId, ct);

            instance.Should().NotBeNull(
                "saga instance written by the first SP must be visible to the fresh SP");
            instance!.State.Should().Be(SagaState.Running,
                "instance persisted by StartAsync begins in Running state");
        }

        // Execute step 0 via the fresh SP (simulates the recovered executor).
        await using (var scope = sp2.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;
            var store = sp.GetRequiredService<ISagaInstanceStore>();
            var registry = sp.GetRequiredService<SagaRegistry>();
            var clock = sp.GetRequiredService<ISystemClock>();
            var uow = sp.GetRequiredService<IUnitOfWork>();

            var instance = await store.FindAsync(sagaId, ct);
            var registration = registry.Find(instance!.SagaName)!;

            // Execute step 0 — in a real recovery the executor would do this.
            await registration.ExecuteStepAsync(
                0, StepDirection.Execute, instance.ContextPayload, sp, ct);

            // Mark the queue entry processed (fetch from store first).
            var pending = await store.FetchPendingStepsAsync(1, ct);
            var entry = pending.FirstOrDefault(e => e.SagaId == sagaId && e.StepIndex == 0);
            entry.Should().NotBeNull();
            entry!.MarkProcessed(clock.UtcNow);
            await store.UpdateStepAsync(entry, ct);
            await uow.SaveChangesAsync(ct);
        }

        // Verify the step-0 entry is now processed in the DB.
        await using (var scope = sp2.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SagaTestDbContext>();
            var entry = await db.Set<SagaStepQueueEntry>()
                .FirstOrDefaultAsync(e => e.SagaId == sagaId && e.StepIndex == 0, ct);

            entry.Should().NotBeNull();
            entry!.ProcessedAt.Should().NotBeNull(
                "step-0 entry must be marked processed after recovery execution");
        }
    }
}
