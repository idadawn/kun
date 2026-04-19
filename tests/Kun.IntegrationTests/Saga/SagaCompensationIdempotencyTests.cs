// ---------------------------------------------------------------------------
// M4.5 — Saga integration tests: compensation idempotency
//
// Verifies that calling CompensateAsync twice on a step is safe — it does not
// corrupt the saga context or throw unexpectedly.
//
// Also verifies that the SagaStepQueueEntry model handles duplicate entries with
// the same (sagaId, stepIndex, Compensate) cleanly — each entry is independent
// and can be stored separately (idempotency is enforced by the step logic, not
// by a unique DB constraint on those columns).
// ---------------------------------------------------------------------------

using FluentAssertions;
using Kun.Core.Time;
using Kun.EntityFrameworkCore;
using Kun.Saga.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Kun.IntegrationTests.Saga;

/// <summary>
/// Compensation idempotency: calling <c>CompensateAsync</c> twice on a step must
/// not corrupt context state or throw, and duplicate queue entries must be storable.
/// </summary>
public sealed class SagaCompensationIdempotencyTests : IAsyncLifetime
{
    private readonly SagaPostgresFixture _fixture = new();
    private ServiceProvider _sp = null!;

    public async ValueTask InitializeAsync()
    {
        await _fixture.InitializeAsync();

        if (string.IsNullOrEmpty(_fixture.ConnectionString))
            return;

        _sp = SagaTestServiceBuilder.Build<SampleSaga>(_fixture.ConnectionString);
        await SagaTestServiceBuilder.EnsureSchemaAsync(_sp, TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_sp is not null) await _sp.DisposeAsync();
        await _fixture.DisposeAsync();
    }

    [Fact]
    public async Task CompensateAsync_CalledTwice_DoesNotCorruptContext()
    {
        var ct = TestContext.Current.CancellationToken;

        if (string.IsNullOrEmpty(_fixture.ConnectionString))
            Assert.Skip("Docker unavailable.");

        var context = new SampleSagaContext
        {
            SagaId = Guid.NewGuid(),
            Step1Compensated = false,
            Step2Compensated = false,
            Step3Compensated = false,
        };

        await using var scope = _sp.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var step1 = sp.GetRequiredService<Step1>();
        var step2 = sp.GetRequiredService<Step2>();
        var step3 = sp.GetRequiredService<Step3>();

        // --- First compensation pass ---
        await step1.CompensateAsync(context, ct);
        await step2.CompensateAsync(context, ct);
        await step3.CompensateAsync(context, ct);

        context.Step1Compensated.Should().BeTrue("step 1 compensated flag set on first call");
        context.Step2Compensated.Should().BeTrue("step 2 compensated flag set on first call");
        context.Step3Compensated.Should().BeTrue("step 3 compensated flag set on first call");

        // --- Second compensation pass (idempotency) ---
        var act = async () =>
        {
            await step1.CompensateAsync(context, ct);
            await step2.CompensateAsync(context, ct);
            await step3.CompensateAsync(context, ct);
        };

        await act.Should().NotThrowAsync(
            "compensate must be idempotent — calling it twice must not throw");

        // Flags remain true — compensation should not un-do itself.
        context.Step1Compensated.Should().BeTrue("flag must stay set after second call");
        context.Step2Compensated.Should().BeTrue("flag must stay set after second call");
        context.Step3Compensated.Should().BeTrue("flag must stay set after second call");
    }

    [Fact]
    public async Task CompensateAsync_WithoutPriorExecute_DoesNotThrow()
    {
        var ct = TestContext.Current.CancellationToken;

        if (string.IsNullOrEmpty(_fixture.ConnectionString))
            Assert.Skip("Docker unavailable.");

        // Compensate a step whose Execute never ran — context flags all false.
        var context = new SampleSagaContext { SagaId = Guid.NewGuid() };

        await using var scope = _sp.CreateAsyncScope();
        var step1 = scope.ServiceProvider.GetRequiredService<Step1>();

        var act = async () => await step1.CompensateAsync(context, ct);

        await act.Should().NotThrowAsync(
            "compensation must be safe even when ExecuteAsync never ran");

        context.Step1Compensated.Should().BeTrue(
            "flag should still be set — compensation is unconditional by design");
    }

    [Fact]
    public async Task DuplicateCompensationQueueEntries_CanBeStoredIndependently()
    {
        var ct = TestContext.Current.CancellationToken;

        if (string.IsNullOrEmpty(_fixture.ConnectionString))
            Assert.Skip("Docker unavailable.");

        // Simulate at-least-once delivery: two Compensate entries for same (sagaId, stepIndex=0).
        // Verifies there is no unique constraint preventing duplicate entries.
        var sagaId = Guid.NewGuid();

        await using (var scope = _sp.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;
            var clock = sp.GetRequiredService<ISystemClock>();
            var store = sp.GetRequiredService<ISagaInstanceStore>();
            var uow = sp.GetRequiredService<IUnitOfWork>();

            // Persist a Compensating saga instance.
            var payload = System.Text.Json.JsonSerializer.Serialize(
                new SampleSagaContext { SagaId = sagaId },
                SampleSagaJsonContext.Default.SampleSagaContext);

            var instance = new SagaInstance(
                id: sagaId,
                sagaName: nameof(SampleSaga),
                tenantId: null,
                contextPayload: payload,
                startedAt: clock.UtcNow,
                timeoutAt: null,
                traceId: null);

            instance.StartCompensating(0, "test-induced", clock.UtcNow);
            await store.AddAsync(instance, ct);

            // Enqueue two Compensate entries for step 0 (simulating duplicate delivery).
            var entry1 = new SagaStepQueueEntry(
                Guid.NewGuid(), sagaId, 0, StepDirection.Compensate, clock.UtcNow);
            var entry2 = new SagaStepQueueEntry(
                Guid.NewGuid(), sagaId, 0, StepDirection.Compensate, clock.UtcNow.AddMilliseconds(1));

            await store.EnqueueStepAsync(entry1, ct);
            await store.EnqueueStepAsync(entry2, ct);
            await uow.SaveChangesAsync(ct);
        }

        // Both entries must be persisted independently (no unique constraint violation).
        await using (var scope = _sp.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SagaTestDbContext>();
            var count = await db.Set<SagaStepQueueEntry>()
                .CountAsync(e => e.SagaId == sagaId && e.Direction == StepDirection.Compensate, ct);

            count.Should().Be(2,
                "two duplicate Compensate entries for the same step must be independently storable — " +
                "idempotency is enforced by step logic, not by a unique DB constraint");
        }

        // Both entries are pending (ProcessedAt == null).
        var pending = await SagaTestServiceBuilder.GetAllQueueEntriesAsync(_sp, sagaId, ct);
        pending.Should().HaveCount(2);
        pending.All(e => e.ProcessedAt == null).Should().BeTrue(
            "entries inserted directly are unprocessed — no executor ran");
    }
}
