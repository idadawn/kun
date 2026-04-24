using FluentAssertions;
using Kun.Audit;
using Kun.Audit.Behaviors;
using Kun.Core.Cqrs;
using Kun.Core.MultiTenancy;
using Kun.Core.Security;
using Kun.Mediator;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Kun.Audit.Tests;

// ---------------------------------------------------------------------------
// Test fixtures
// ---------------------------------------------------------------------------

internal sealed record SampleCommand(string Payload) : IRequest<string>;

internal sealed class StubTenantContext : ITenantContext
{
    public StubTenantContext(string? tenantId) { TenantId = tenantId; IsMultiTenant = tenantId is not null; }
    public string? TenantId { get; }
    public bool IsMultiTenant { get; }
}

internal sealed class StubCurrentUser : ICurrentUser
{
    public StubCurrentUser(string? userId, bool isAuthenticated = true)
    {
        UserId = userId;
        IsAuthenticated = isAuthenticated;
    }
    public string? UserId { get; }
    public IReadOnlyList<string> Roles { get; } = [];
    public bool IsAuthenticated { get; }
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

public sealed class AuditLogPipelineBehaviorTests
{
    private static AuditLogPipelineBehavior<SampleCommand, string> Build(
        InMemoryAuditSink sink,
        InMemoryAuditChainState chain,
        string? tenantId = "t-a",
        string? userId = "u-1")
    {
        return new AuditLogPipelineBehavior<SampleCommand, string>(
            sink,
            chain,
            new StubTenantContext(tenantId),
            new StubCurrentUser(userId),
            TimeProvider.System,
            NullLogger<AuditLogPipelineBehavior<SampleCommand, string>>.Instance);
    }

    [Fact]
    public async Task Handle_Successful_Handler_Writes_Success_Entry()
    {
        var sink = new InMemoryAuditSink();
        using var chain = new InMemoryAuditChainState();
        var behavior = Build(sink, chain);

        var response = await behavior.Handle(
            new SampleCommand("hi"),
            () => ValueTask.FromResult("ok"),
            TestContext.Current.CancellationToken);

        response.Should().Be("ok");
        sink.Entries.Should().ContainSingle();
        var entry = sink.Entries.Single();
        entry.CommandName.Should().Be(nameof(SampleCommand));
        entry.Outcome.Should().Be(AuditOutcome.Success);
        entry.ErrorCode.Should().BeNull();
        entry.PrevHash.Should().BeNull();
        entry.TenantId.Should().Be("t-a");
        entry.ActorId.Should().Be("u-1");
    }

    [Fact]
    public async Task Handle_Exception_Handler_Writes_Exception_Entry_And_Rethrows_Original()
    {
        var sink = new InMemoryAuditSink();
        using var chain = new InMemoryAuditChainState();
        var behavior = Build(sink, chain);

        Func<Task> act = async () => await behavior.Handle(
            new SampleCommand("hi"),
            () => throw new InvalidOperationException("boom"),
            TestContext.Current.CancellationToken);

        var thrown = await act.Should().ThrowAsync<InvalidOperationException>();
        thrown.Which.Message.Should().Be("boom");

        sink.Entries.Should().ContainSingle();
        var entry = sink.Entries.Single();
        entry.Outcome.Should().Be(AuditOutcome.Exception);
        entry.ErrorCode.Should().Be(nameof(InvalidOperationException));
    }

    [Fact]
    public async Task Handle_Sink_Failure_Does_Not_Break_Successful_Handler()
    {
        var sink = Substitute.For<IAuditSink>();
        sink.WriteAsync(Arg.Any<AuditEntry>(), Arg.Any<CancellationToken>())
            .Returns<ValueTask>(_ => throw new InvalidOperationException("sink offline"));
        using var chain = new InMemoryAuditChainState();

        var behavior = new AuditLogPipelineBehavior<SampleCommand, string>(
            sink,
            chain,
            new StubTenantContext("t"),
            new StubCurrentUser("u"),
            TimeProvider.System,
            NullLogger<AuditLogPipelineBehavior<SampleCommand, string>>.Instance);

        var response = await behavior.Handle(
            new SampleCommand("hi"),
            () => ValueTask.FromResult("ok"),
            TestContext.Current.CancellationToken);

        response.Should().Be("ok",
            "audit-side failure must never fail a business-successful handler");
    }

    [Fact]
    public async Task Handle_Sink_Failure_Does_Not_Replace_Original_Exception()
    {
        var sink = Substitute.For<IAuditSink>();
        sink.WriteAsync(Arg.Any<AuditEntry>(), Arg.Any<CancellationToken>())
            .Returns<ValueTask>(_ => throw new InvalidOperationException("sink offline"));
        using var chain = new InMemoryAuditChainState();

        var behavior = new AuditLogPipelineBehavior<SampleCommand, string>(
            sink,
            chain,
            new StubTenantContext("t"),
            new StubCurrentUser("u"),
            TimeProvider.System,
            NullLogger<AuditLogPipelineBehavior<SampleCommand, string>>.Instance);

        Func<Task> act = async () => await behavior.Handle(
            new SampleCommand("hi"),
            () => throw new ArgumentException("business"),
            TestContext.Current.CancellationToken);

        var thrown = await act.Should().ThrowAsync<ArgumentException>();
        thrown.Which.Message.Should().Be("business",
            "business exception must always reach the caller, unmasked by sink failure");
    }

    [Fact]
    public async Task Handle_Consecutive_Calls_Chain_Entries()
    {
        var sink = new InMemoryAuditSink();
        using var chain = new InMemoryAuditChainState();
        var behavior = Build(sink, chain);
        var ct = TestContext.Current.CancellationToken;

        await behavior.Handle(new SampleCommand("a"), () => ValueTask.FromResult("1"), ct);
        await behavior.Handle(new SampleCommand("b"), () => ValueTask.FromResult("2"), ct);
        await behavior.Handle(new SampleCommand("c"), () => ValueTask.FromResult("3"), ct);

        var entries = sink.Entries.ToArray();
        entries.Should().HaveCount(3);
        entries[0].PrevHash.Should().BeNull();
        entries[1].PrevHash.Should().Be(entries[0].ContentHash,
            "second entry must link to first");
        entries[2].PrevHash.Should().Be(entries[1].ContentHash,
            "third entry must link to second");
    }

    [Fact]
    public async Task Handle_Different_Tenants_Produce_Independent_Chains()
    {
        var sink = new InMemoryAuditSink();
        using var chain = new InMemoryAuditChainState();
        var ct = TestContext.Current.CancellationToken;

        var a = Build(sink, chain, tenantId: "tenant-a");
        var b = Build(sink, chain, tenantId: "tenant-b");

        await a.Handle(new SampleCommand("x"), () => ValueTask.FromResult("1"), ct);
        await b.Handle(new SampleCommand("y"), () => ValueTask.FromResult("2"), ct);
        await a.Handle(new SampleCommand("z"), () => ValueTask.FromResult("3"), ct);

        var aEntries = sink.Entries.Where(e => e.TenantId == "tenant-a").ToArray();
        var bEntries = sink.Entries.Where(e => e.TenantId == "tenant-b").ToArray();

        aEntries.Should().HaveCount(2);
        bEntries.Should().HaveCount(1);

        aEntries[0].PrevHash.Should().BeNull();
        aEntries[1].PrevHash.Should().Be(aEntries[0].ContentHash,
            "tenant-a's chain must link within itself");
        bEntries[0].PrevHash.Should().BeNull(
            "tenant-b's chain must start fresh, not link to tenant-a's tip");
    }
}
