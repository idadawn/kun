using Kun.EventBus.Outbox;
using Microsoft.EntityFrameworkCore;

namespace Kun.Testing.EntityFrameworkCore.Outbox;

/// <summary>
/// Fluent assertion extensions for EF-persisted <see cref="OutboxMessage"/>
/// state. Helpers come in two overload families:
/// one on <see cref="IEnumerable{T}"/> of <see cref="OutboxMessage"/> for
/// tests that already hold a pre-queried snapshot, and one on
/// <see cref="DbContext"/> for the one-liner case. On mismatch, helpers
/// throw <see cref="KunAssertionException"/> with a precise message that
/// includes the helper name, expected <c>eventTypeId</c>, observed count,
/// and the distinct event-type IDs actually present (so the failure
/// message tells the consumer what WAS staged, not just what wasn't).
/// See [ADR-015 §F3](../../docs/architecture/ADR-015-Outbox-pattern.md).
/// </summary>
public static class OutboxAssertions
{
    // ── IEnumerable<OutboxMessage> overloads ─────────────────────────────

    /// <summary>
    /// Asserts the outbox contains at least one <see cref="OutboxMessage"/>
    /// whose <see cref="OutboxMessage.EventTypeId"/> equals <paramref name="eventTypeId"/>
    /// (ordinal comparison).
    /// </summary>
    public static void ShouldHaveOutboxMessage(
        this IEnumerable<OutboxMessage> outbox,
        string eventTypeId)
    {
        ArgumentNullException.ThrowIfNull(outbox);
        ArgumentNullException.ThrowIfNull(eventTypeId);

        var snapshot = outbox.ToArray();
        var matches = CountMatches(snapshot, eventTypeId);
        if (matches > 0) return;

        throw new KunAssertionException(
            $"ShouldHaveOutboxMessage — expected ≥1 message with EventTypeId={eventTypeId} " +
            $"but saw 0 (total messages={snapshot.Length}, distinct EventTypeIds={FormatDistinctEventTypeIds(snapshot)}).");
    }

    /// <summary>
    /// Asserts the outbox contains exactly one <see cref="OutboxMessage"/>
    /// whose <see cref="OutboxMessage.EventTypeId"/> equals <paramref name="eventTypeId"/>,
    /// and returns it for further inspection.
    /// </summary>
    public static OutboxMessage ShouldHaveExactlyOneOutboxMessage(
        this IEnumerable<OutboxMessage> outbox,
        string eventTypeId)
    {
        ArgumentNullException.ThrowIfNull(outbox);
        ArgumentNullException.ThrowIfNull(eventTypeId);

        var snapshot = outbox.ToArray();
        var matches = FilterMatches(snapshot, eventTypeId);
        if (matches.Length == 1) return matches[0];

        throw new KunAssertionException(
            $"ShouldHaveExactlyOneOutboxMessage — expected exactly 1 message with " +
            $"EventTypeId={eventTypeId} but saw {matches.Length} " +
            $"(total messages={snapshot.Length}, distinct EventTypeIds={FormatDistinctEventTypeIds(snapshot)}).");
    }

    /// <summary>
    /// Asserts the outbox contains no <see cref="OutboxMessage"/> whose
    /// <see cref="OutboxMessage.EventTypeId"/> equals <paramref name="eventTypeId"/>.
    /// </summary>
    public static void ShouldNotHaveOutboxMessage(
        this IEnumerable<OutboxMessage> outbox,
        string eventTypeId)
    {
        ArgumentNullException.ThrowIfNull(outbox);
        ArgumentNullException.ThrowIfNull(eventTypeId);

        var snapshot = outbox.ToArray();
        var matches = CountMatches(snapshot, eventTypeId);
        if (matches == 0) return;

        throw new KunAssertionException(
            $"ShouldNotHaveOutboxMessage — expected 0 messages with EventTypeId={eventTypeId} " +
            $"but saw {matches} (total messages={snapshot.Length}).");
    }

    // ── DbContext overloads ──────────────────────────────────────────────

    /// <summary>
    /// Asserts the DbContext's <see cref="OutboxMessage"/> set contains
    /// at least one message with the given <paramref name="eventTypeId"/>.
    /// Equivalent to
    /// <c>db.Set&lt;OutboxMessage&gt;().AsEnumerable().ShouldHaveOutboxMessage(eventTypeId)</c>.
    /// </summary>
    public static void ShouldHaveOutboxMessage(
        this DbContext db,
        string eventTypeId)
    {
        ArgumentNullException.ThrowIfNull(db);
        db.Set<OutboxMessage>().AsEnumerable().ShouldHaveOutboxMessage(eventTypeId);
    }

    /// <summary>
    /// Asserts the DbContext's <see cref="OutboxMessage"/> set contains
    /// exactly one message with the given <paramref name="eventTypeId"/>,
    /// and returns it.
    /// </summary>
    public static OutboxMessage ShouldHaveExactlyOneOutboxMessage(
        this DbContext db,
        string eventTypeId)
    {
        ArgumentNullException.ThrowIfNull(db);
        return db.Set<OutboxMessage>().AsEnumerable().ShouldHaveExactlyOneOutboxMessage(eventTypeId);
    }

    /// <summary>
    /// Asserts the DbContext's <see cref="OutboxMessage"/> set contains
    /// no messages with the given <paramref name="eventTypeId"/>.
    /// </summary>
    public static void ShouldNotHaveOutboxMessage(
        this DbContext db,
        string eventTypeId)
    {
        ArgumentNullException.ThrowIfNull(db);
        db.Set<OutboxMessage>().AsEnumerable().ShouldNotHaveOutboxMessage(eventTypeId);
    }

    // ── private helpers ──────────────────────────────────────────────────

    private static int CountMatches(OutboxMessage[] snapshot, string eventTypeId)
    {
        var count = 0;
        for (var i = 0; i < snapshot.Length; i++)
            if (string.Equals(snapshot[i].EventTypeId, eventTypeId, StringComparison.Ordinal))
                count++;
        return count;
    }

    private static OutboxMessage[] FilterMatches(OutboxMessage[] snapshot, string eventTypeId)
    {
        var matches = new List<OutboxMessage>();
        for (var i = 0; i < snapshot.Length; i++)
            if (string.Equals(snapshot[i].EventTypeId, eventTypeId, StringComparison.Ordinal))
                matches.Add(snapshot[i]);
        return matches.ToArray();
    }

    private static string FormatDistinctEventTypeIds(OutboxMessage[] snapshot)
    {
        if (snapshot.Length == 0) return "[]";
        var distinct = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < snapshot.Length; i++)
            distinct.Add(snapshot[i].EventTypeId);
        return "[" + string.Join(", ", distinct) + "]";
    }
}
