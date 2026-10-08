using Xunit;

namespace Kun.Audit.Tests;

public sealed class AgentAuditHashTests
{
    [Fact]
    public void Hash_ActorTypeIsCovered_AndLegacyEntriesRemainReplayable()
    {
        var entry = new AuditEntry(Guid.Parse("12345678-1234-1234-1234-123456789012"),
            "tenant-a", "actor-1", "Run", DateTimeOffset.UnixEpoch, 1, AuditOutcome.Success, null, null, "");
        Assert.Equal(AuditContentHasher.Compute(entry.Id, entry.TenantId, entry.ActorId, entry.CommandName,
            entry.OccurredAt, entry.LatencyMs, entry.Outcome, entry.ErrorCode, entry.PrevHash), AuditContentHasher.Compute(entry));
        var agent = entry with { ActorType = AuditActorType.Agent };
        Assert.Equal("agent", agent.ActorType);
        Assert.NotEqual(AuditContentHasher.Compute(agent), AuditContentHasher.Compute(agent with { ActorType = AuditActorType.User }));
        Assert.NotEqual(AuditContentHasher.Compute(agent with { ActorId = "x|y", CommandName = "z" }),
            AuditContentHasher.Compute(agent with { ActorId = "x", CommandName = "y|z" }));
    }
}
