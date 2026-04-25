using Kun.Core.Domain;

namespace Kun.Testing.Domain;

/// <summary>
/// Fluent assertion extensions for <see cref="IAggregateRoot"/> domain-
/// event collections. On mismatch, throws
/// <see cref="KunAssertionException"/> with a precise message per the
/// ADR-012 §Error-message contract.
/// </summary>
/// <remarks>
/// Uses <see cref="System.Linq.Enumerable.OfType{TResult}"/> — compile-
/// time-safe and AOT-friendly (no reflection, no
/// <c>MakeGenericMethod</c>).
/// </remarks>
public static class DomainEventAssertions
{
    /// <summary>
    /// Asserts the aggregate raised exactly one event of type
    /// <typeparamref name="TEvent"/>. Returns the event for further
    /// inspection.
    /// </summary>
    public static TEvent ShouldRaise<TEvent>(this IAggregateRoot aggregate)
        where TEvent : IDomainEvent
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        var matching = aggregate.DomainEvents.OfType<TEvent>().ToList();
        if (matching.Count == 1) return matching[0];
        throw new KunAssertionException(
            $"ShouldRaise<{typeof(TEvent).Name}> — expected exactly 1 but saw {matching.Count} event(s) of that type. Actual events: [{FormatEventTypes(aggregate.DomainEvents)}].");
    }

    /// <summary>
    /// Asserts the aggregate raised exactly <paramref name="count"/>
    /// events of type <typeparamref name="TEvent"/>. Returns them as a
    /// read-only list preserving the original emission order.
    /// </summary>
    public static IReadOnlyList<TEvent> ShouldRaiseExactly<TEvent>(this IAggregateRoot aggregate, int count)
        where TEvent : IDomainEvent
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count), count, "Expected count must be ≥ 0.");
        var matching = aggregate.DomainEvents.OfType<TEvent>().ToList();
        if (matching.Count == count) return matching;
        throw new KunAssertionException(
            $"ShouldRaiseExactly<{typeof(TEvent).Name}> — expected {count} but saw {matching.Count} event(s) of that type. Actual events: [{FormatEventTypes(aggregate.DomainEvents)}].");
    }

    /// <summary>
    /// Asserts the aggregate raised zero events of type
    /// <typeparamref name="TEvent"/>.
    /// </summary>
    public static void ShouldNotRaise<TEvent>(this IAggregateRoot aggregate)
        where TEvent : IDomainEvent
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        var count = aggregate.DomainEvents.OfType<TEvent>().Count();
        if (count == 0) return;
        throw new KunAssertionException(
            $"ShouldNotRaise<{typeof(TEvent).Name}> — expected 0 but saw {count} event(s) of that type. Actual events: [{FormatEventTypes(aggregate.DomainEvents)}].");
    }

    private static string FormatEventTypes(IReadOnlyCollection<IDomainEvent> events)
    {
        if (events.Count == 0) return "<none>";
        return string.Join(", ", events.Select(e => e.GetType().Name));
    }
}
