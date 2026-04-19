using FluentAssertions;
using Kun.Core.Domain;
using Xunit;

namespace Kun.Core.Tests.Domain;

file sealed class TestAggregate : AggregateRoot<Guid>
{
    public void AddTestEvent(IDomainEvent @event) => AddDomainEvent(@event);
}

file sealed record TestEvent(DateTime OccurredAt) : IDomainEvent;

public class AggregateRootTests
{
    [Fact]
    public void AddDomainEvent_AddsEventToDomainEvents()
    {
        // Arrange
        var aggregate = new TestAggregate();
        var @event = new TestEvent(DateTime.UtcNow);

        // Act
        aggregate.AddTestEvent(@event);

        // Assert
        aggregate.DomainEvents.Should().ContainSingle()
            .Which.Should().BeSameAs(@event);
    }

    [Fact]
    public void DomainEvents_ExposedAsReadOnlyCollection()
    {
        // Arrange
        var aggregate = new TestAggregate();
        aggregate.AddTestEvent(new TestEvent(DateTime.UtcNow));

        // Act
        var events = aggregate.DomainEvents;

        // Assert
        events.Should().BeAssignableTo<IReadOnlyCollection<IDomainEvent>>();
    }

    [Fact]
    public void ClearDomainEvents_RemovesAllEvents()
    {
        // Arrange
        var aggregate = new TestAggregate();
        aggregate.AddTestEvent(new TestEvent(DateTime.UtcNow));
        aggregate.AddTestEvent(new TestEvent(DateTime.UtcNow));

        // Act
        aggregate.ClearDomainEvents();

        // Assert
        aggregate.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Delete_SetsIsDeletedAndDeletedAt()
    {
        // Arrange
        var aggregate = new TestAggregate();
        var before = DateTime.UtcNow;

        // Act
        aggregate.Delete();

        // Assert
        aggregate.IsDeleted.Should().BeTrue();
        aggregate.DeletedAt.Should().NotBeNull();
        aggregate.DeletedAt!.Value.Should().BeOnOrAfter(before);
    }
}
