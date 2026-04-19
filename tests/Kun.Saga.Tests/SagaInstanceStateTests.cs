using FluentAssertions;
using Kun.Saga.Persistence;
using Xunit;

namespace Kun.Saga.Tests;

public sealed class SagaInstanceStateTests
{
    private static SagaInstance CreateRunningInstance(int stepIndex = 0)
    {
        var instance = new SagaInstance(
            id: Guid.NewGuid(),
            sagaName: "TestSaga",
            tenantId: null,
            contextPayload: "{}",
            startedAt: DateTimeOffset.UtcNow,
            timeoutAt: DateTimeOffset.UtcNow.AddMinutes(5),
            traceId: null);

        // Move to the desired step index if > 0
        for (var i = 1; i <= stepIndex; i++)
        {
            instance.MoveToStep(i, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(5), "{}");
        }

        return instance;
    }

    [Fact]
    public void NewInstance_HasRunningStateAtStepZero()
    {
        var instance = CreateRunningInstance();

        instance.State.Should().Be(SagaState.Running);
        instance.CurrentStepIndex.Should().Be(0);
        instance.CompletedAt.Should().BeNull();
        instance.FailureReason.Should().BeNull();
    }

    [Fact]
    public void MarkCompleted_TransitionsToCompleted()
    {
        var instance = CreateRunningInstance();
        var completedAt = DateTimeOffset.UtcNow;

        instance.MarkCompleted(completedAt);

        instance.State.Should().Be(SagaState.Completed);
        instance.CompletedAt.Should().Be(completedAt);
        instance.TimeoutAt.Should().BeNull();
    }

    [Fact]
    public void MarkCompleted_WhenNotRunning_Throws()
    {
        var instance = CreateRunningInstance();
        instance.MarkCompleted(DateTimeOffset.UtcNow);

        var act = () => instance.MarkCompleted(DateTimeOffset.UtcNow);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Completed*");
    }

    [Fact]
    public void StartCompensating_TransitionsToCompensating()
    {
        var instance = CreateRunningInstance(stepIndex: 2);
        var now = DateTimeOffset.UtcNow;

        instance.StartCompensating(2, "step 2 failed", now);

        instance.State.Should().Be(SagaState.Compensating);
        instance.CurrentStepIndex.Should().Be(2);
        instance.FailureReason.Should().Be("step 2 failed");
        instance.TimeoutAt.Should().BeNull();
    }

    [Fact]
    public void StartCompensating_WhenAlreadyCompensating_Throws()
    {
        var instance = CreateRunningInstance();
        instance.StartCompensating(0, "failure", DateTimeOffset.UtcNow);

        var act = () => instance.StartCompensating(0, "again", DateTimeOffset.UtcNow);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void MarkCompensated_TransitionsToCompensated()
    {
        var instance = CreateRunningInstance();
        instance.StartCompensating(0, "failure", DateTimeOffset.UtcNow);
        var now = DateTimeOffset.UtcNow;

        instance.MarkCompensated(now);

        instance.State.Should().Be(SagaState.Compensated);
        instance.CompletedAt.Should().Be(now);
    }

    [Fact]
    public void MarkCompensated_WhenNotCompensating_Throws()
    {
        var instance = CreateRunningInstance();

        var act = () => instance.MarkCompensated(DateTimeOffset.UtcNow);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*mark compensated*");
    }

    [Fact]
    public void Fail_TransitionsToFailed_FromAnyState()
    {
        var instance = CreateRunningInstance();
        var now = DateTimeOffset.UtcNow;

        instance.Fail("catastrophic failure", now);

        instance.State.Should().Be(SagaState.Failed);
        instance.FailureReason.Should().Be("catastrophic failure");
        instance.TimeoutAt.Should().BeNull();
    }

    [Fact]
    public void MoveToStep_WhenRunning_UpdatesIndex()
    {
        var instance = CreateRunningInstance();
        var now = DateTimeOffset.UtcNow;

        instance.MoveToStep(1, now, now.AddMinutes(5), "{\"step\":1}");

        instance.CurrentStepIndex.Should().Be(1);
        instance.ContextPayload.Should().Be("{\"step\":1}");
    }

    [Fact]
    public void MoveToStep_WhenNotRunning_Throws()
    {
        var instance = CreateRunningInstance();
        instance.MarkCompleted(DateTimeOffset.UtcNow);

        var act = () => instance.MoveToStep(1, DateTimeOffset.UtcNow, null, "{}");

        act.Should().Throw<InvalidOperationException>();
    }
}
