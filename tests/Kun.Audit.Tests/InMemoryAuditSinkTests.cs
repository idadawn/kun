using FluentAssertions;
using Xunit;

namespace Kun.Audit.Tests;

public sealed class InMemoryAuditSinkTests
{
    private static AuditEntry MakeEntry(string commandName) => new(
        Id: Guid.NewGuid(),
        TenantId: "t",
        ActorId: "u",
        CommandName: commandName,
        OccurredAt: DateTimeOffset.UtcNow,
        LatencyMs: 1,
        Outcome: AuditOutcome.Success,
        ErrorCode: null,
        PrevHash: null,
        ContentHash: "hash");

    [Fact]
    public async Task WriteAsync_Stores_Entries_In_Order()
    {
        var sink = new InMemoryAuditSink();
        var ct = TestContext.Current.CancellationToken;

        await sink.WriteAsync(MakeEntry("A"), ct);
        await sink.WriteAsync(MakeEntry("B"), ct);
        await sink.WriteAsync(MakeEntry("C"), ct);

        sink.Entries.Select(e => e.CommandName).Should().Equal("A", "B", "C");
    }

    [Fact]
    public async Task WriteAsync_Null_Entry_Throws()
    {
        var sink = new InMemoryAuditSink();
        Func<Task> act = async () => await sink.WriteAsync(null!, TestContext.Current.CancellationToken);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task Clear_Empties_Entries()
    {
        var sink = new InMemoryAuditSink();
        await sink.WriteAsync(MakeEntry("A"), TestContext.Current.CancellationToken);

        sink.Clear();

        sink.Entries.Should().BeEmpty();
    }
}
