using FluentAssertions;
using Kun.Saga.Persistence;
using Xunit;

namespace Kun.Saga.Tests;

public sealed class SagaStepQueueEntryTests
{
    private static SagaStepQueueEntry CreateEntry(StepDirection direction = StepDirection.Execute)
        => new(
            id: Guid.NewGuid(),
            sagaId: Guid.NewGuid(),
            stepIndex: 0,
            direction: direction,
            enqueuedAt: DateTimeOffset.UtcNow);

    [Fact]
    public void NewEntry_HasZeroAttemptsAndNullProcessedAt()
    {
        var entry = CreateEntry();

        entry.AttemptCount.Should().Be(0);
        entry.ProcessedAt.Should().BeNull();
        entry.Direction.Should().Be(StepDirection.Execute);
    }

    [Fact]
    public void IncrementAttempt_IncrementsAttemptCount()
    {
        var entry = CreateEntry();

        entry.IncrementAttempt();
        entry.IncrementAttempt();

        entry.AttemptCount.Should().Be(2);
    }

    [Fact]
    public void MarkProcessed_SetsProcessedAt()
    {
        var entry = CreateEntry();
        var now = DateTimeOffset.UtcNow;

        entry.MarkProcessed(now);

        entry.ProcessedAt.Should().Be(now);
    }

    [Fact]
    public void CompensateEntry_HasCorrectDirection()
    {
        var entry = CreateEntry(StepDirection.Compensate);

        entry.Direction.Should().Be(StepDirection.Compensate);
    }
}
