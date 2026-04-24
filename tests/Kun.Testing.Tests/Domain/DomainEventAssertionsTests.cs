using FluentAssertions;
using Kun.Core.Domain;
using Kun.Testing.Domain;
using Xunit;

namespace Kun.Testing.Tests.Domain;

public class DomainEventAssertionsTests
{
    private sealed record OrderPlacedEvent(DateTime OccurredAt) : IDomainEvent;
    private sealed record OrderShippedEvent(DateTime OccurredAt) : IDomainEvent;

    private sealed class TestAggregate : AggregateRoot<Guid, Guid>
    {
        public void Raise(IDomainEvent evt) => AddDomainEvent(evt);
    }

    private static TestAggregate NewAggregate(params IDomainEvent[] events)
    {
        var a = new TestAggregate();
        foreach (var e in events) a.Raise(e);
        return a;
    }

    // ── ShouldRaise ─────────────────────────────────────────────────────────

    [Fact]
    public void ShouldRaise_ExactlyOneMatchingEvent_ReturnsTheEvent()
    {
        var placed = new OrderPlacedEvent(DateTime.UtcNow);
        var agg = NewAggregate(placed);
        var result = agg.ShouldRaise<OrderPlacedEvent>();
        result.Should().BeSameAs(placed);
    }

    [Fact]
    public void ShouldRaise_NoMatchingEvent_ThrowsWithObservedList()
    {
        var agg = NewAggregate(new OrderShippedEvent(DateTime.UtcNow));
        var act = () => agg.ShouldRaise<OrderPlacedEvent>();
        act.Should().Throw<KunAssertionException>()
            .WithMessage("*ShouldRaise<OrderPlacedEvent>*expected exactly 1 but saw 0*OrderShippedEvent*");
    }

    [Fact]
    public void ShouldRaise_MultipleMatchingEvents_ThrowsWithCount()
    {
        var agg = NewAggregate(
            new OrderPlacedEvent(DateTime.UtcNow),
            new OrderPlacedEvent(DateTime.UtcNow));
        var act = () => agg.ShouldRaise<OrderPlacedEvent>();
        act.Should().Throw<KunAssertionException>()
            .WithMessage("*ShouldRaise<OrderPlacedEvent>*expected exactly 1 but saw 2*");
    }

    [Fact]
    public void ShouldRaise_NullAggregate_ThrowsArgumentNull()
    {
        IAggregateRoot agg = null!;
        var act = () => agg.ShouldRaise<OrderPlacedEvent>();
        act.Should().Throw<ArgumentNullException>();
    }

    // ── ShouldRaiseExactly ──────────────────────────────────────────────────

    [Fact]
    public void ShouldRaiseExactly_MatchingCount_ReturnsEventsInOrder()
    {
        var first = new OrderPlacedEvent(DateTime.UtcNow);
        var second = new OrderPlacedEvent(DateTime.UtcNow.AddSeconds(1));
        var agg = NewAggregate(first, second);

        var matching = agg.ShouldRaiseExactly<OrderPlacedEvent>(2);

        matching.Should().Equal(first, second);
    }

    [Fact]
    public void ShouldRaiseExactly_Zero_WhenAggregateRaisedNoneOfThatType_Passes()
    {
        var agg = NewAggregate(new OrderShippedEvent(DateTime.UtcNow));
        var matching = agg.ShouldRaiseExactly<OrderPlacedEvent>(0);
        matching.Should().BeEmpty();
    }

    [Fact]
    public void ShouldRaiseExactly_Mismatch_ThrowsWithExpectedAndActual()
    {
        var agg = NewAggregate(new OrderPlacedEvent(DateTime.UtcNow));
        var act = () => agg.ShouldRaiseExactly<OrderPlacedEvent>(3);
        act.Should().Throw<KunAssertionException>()
            .WithMessage("*ShouldRaiseExactly<OrderPlacedEvent>*expected 3 but saw 1*");
    }

    [Fact]
    public void ShouldRaiseExactly_NegativeCount_ThrowsArgumentOutOfRange()
    {
        var agg = NewAggregate();
        var act = () => agg.ShouldRaiseExactly<OrderPlacedEvent>(-1);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // ── ShouldNotRaise ──────────────────────────────────────────────────────

    [Fact]
    public void ShouldNotRaise_NoEventsOfThatType_DoesNotThrow()
    {
        var agg = NewAggregate(new OrderShippedEvent(DateTime.UtcNow));
        var act = () => agg.ShouldNotRaise<OrderPlacedEvent>();
        act.Should().NotThrow();
    }

    [Fact]
    public void ShouldNotRaise_EmptyAggregate_DoesNotThrow()
    {
        var agg = NewAggregate();
        var act = () => agg.ShouldNotRaise<OrderPlacedEvent>();
        act.Should().NotThrow();
    }

    [Fact]
    public void ShouldNotRaise_OneMatchingEvent_ThrowsWithObservedList()
    {
        var agg = NewAggregate(
            new OrderPlacedEvent(DateTime.UtcNow),
            new OrderShippedEvent(DateTime.UtcNow));
        var act = () => agg.ShouldNotRaise<OrderPlacedEvent>();
        act.Should().Throw<KunAssertionException>()
            .WithMessage("*ShouldNotRaise<OrderPlacedEvent>*expected 0 but saw 1*OrderPlacedEvent*OrderShippedEvent*");
    }

    // ── Message shape ───────────────────────────────────────────────────────

    [Fact]
    public void EmptyAggregate_FailureMessage_ContainsNoneSentinel()
    {
        var agg = NewAggregate();
        var act = () => agg.ShouldRaise<OrderPlacedEvent>();
        act.Should().Throw<KunAssertionException>()
            .WithMessage("*<none>*");
    }
}
