using FluentAssertions;
using Kun.EventBus.Outbox;
using Kun.Testing.EntityFrameworkCore.Outbox;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Kun.Testing.EntityFrameworkCore.Tests;

public sealed class OutboxAssertionsTests
{
    // ── IEnumerable<OutboxMessage>.ShouldHaveOutboxMessage ────────────────

    [Fact]
    public void IEnumerable_ShouldHave_passes_when_one_match_present()
    {
        var outbox = new[] { Msg("order.placed.v1"), Msg("payment.refunded.v1") };
        var act = () => outbox.ShouldHaveOutboxMessage("order.placed.v1");
        act.Should().NotThrow();
    }

    [Fact]
    public void IEnumerable_ShouldHave_passes_when_multiple_matches_present()
    {
        var outbox = new[] { Msg("order.placed.v1"), Msg("order.placed.v1") };
        var act = () => outbox.ShouldHaveOutboxMessage("order.placed.v1");
        act.Should().NotThrow();
    }

    [Fact]
    public void IEnumerable_ShouldHave_throws_when_no_match()
    {
        var outbox = new[] { Msg("payment.refunded.v1") };
        var act = () => outbox.ShouldHaveOutboxMessage("order.placed.v1");
        act.Should().Throw<KunAssertionException>()
            .WithMessage("*ShouldHaveOutboxMessage*order.placed.v1*payment.refunded.v1*");
    }

    // ── IEnumerable<OutboxMessage>.ShouldHaveExactlyOneOutboxMessage ──────

    [Fact]
    public void IEnumerable_ShouldHaveExactlyOne_passes_when_exactly_one_match()
    {
        var outbox = new[] { Msg("order.placed.v1"), Msg("payment.refunded.v1") };
        var returned = outbox.ShouldHaveExactlyOneOutboxMessage("order.placed.v1");
        returned.EventTypeId.Should().Be("order.placed.v1");
    }

    [Fact]
    public void IEnumerable_ShouldHaveExactlyOne_throws_when_zero_matches()
    {
        var outbox = new[] { Msg("payment.refunded.v1") };
        var act = () => outbox.ShouldHaveExactlyOneOutboxMessage("order.placed.v1");
        act.Should().Throw<KunAssertionException>()
            .WithMessage("*ShouldHaveExactlyOneOutboxMessage*order.placed.v1*0*");
    }

    [Fact]
    public void IEnumerable_ShouldHaveExactlyOne_throws_when_two_matches()
    {
        var outbox = new[] { Msg("order.placed.v1"), Msg("order.placed.v1") };
        var act = () => outbox.ShouldHaveExactlyOneOutboxMessage("order.placed.v1");
        act.Should().Throw<KunAssertionException>()
            .WithMessage("*ShouldHaveExactlyOneOutboxMessage*order.placed.v1*2*");
    }

    // ── IEnumerable<OutboxMessage>.ShouldNotHaveOutboxMessage ─────────────

    [Fact]
    public void IEnumerable_ShouldNotHave_passes_when_zero_matches()
    {
        var outbox = new[] { Msg("payment.refunded.v1") };
        var act = () => outbox.ShouldNotHaveOutboxMessage("order.placed.v1");
        act.Should().NotThrow();
    }

    [Fact]
    public void IEnumerable_ShouldNotHave_throws_when_match_present()
    {
        var outbox = new[] { Msg("order.placed.v1") };
        var act = () => outbox.ShouldNotHaveOutboxMessage("order.placed.v1");
        act.Should().Throw<KunAssertionException>()
            .WithMessage("*ShouldNotHaveOutboxMessage*order.placed.v1*1*");
    }

    // ── DbContext overloads: parity checks with InMemoryDatabase ──────────

    [Fact]
    public void DbContext_ShouldHave_delegates_to_enumerable_overload()
    {
        using var db = NewInMemoryContext();
        db.Outbox.Add(Msg("order.placed.v1"));
        db.SaveChanges();

        db.ShouldHaveOutboxMessage("order.placed.v1");
        var act = () => db.ShouldHaveOutboxMessage("payment.refunded.v1");
        act.Should().Throw<KunAssertionException>();
    }

    [Fact]
    public void DbContext_ShouldHaveExactlyOne_delegates_and_returns_match()
    {
        using var db = NewInMemoryContext();
        db.Outbox.Add(Msg("order.placed.v1"));
        db.Outbox.Add(Msg("payment.refunded.v1"));
        db.SaveChanges();

        var match = db.ShouldHaveExactlyOneOutboxMessage("order.placed.v1");
        match.EventTypeId.Should().Be("order.placed.v1");
    }

    [Fact]
    public void DbContext_ShouldNotHave_delegates_to_enumerable_overload()
    {
        using var db = NewInMemoryContext();
        db.Outbox.Add(Msg("payment.refunded.v1"));
        db.SaveChanges();

        db.ShouldNotHaveOutboxMessage("order.placed.v1");
        var act = () => db.ShouldNotHaveOutboxMessage("payment.refunded.v1");
        act.Should().Throw<KunAssertionException>();
    }

    // ── fixtures ──────────────────────────────────────────────────────────

    private static OutboxMessage Msg(string eventTypeId) => new(
        id: Guid.NewGuid(),
        eventTypeId: eventTypeId,
        payload: "{}",
        tenantId: null,
        traceParent: null,
        traceState: null,
        occurredAt: DateTimeOffset.UtcNow);

    private static OutboxTestDbContext NewInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<OutboxTestDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new OutboxTestDbContext(options);
    }

    private sealed class OutboxTestDbContext : DbContext
    {
        public OutboxTestDbContext(DbContextOptions<OutboxTestDbContext> options) : base(options) { }
        public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();
    }
}
