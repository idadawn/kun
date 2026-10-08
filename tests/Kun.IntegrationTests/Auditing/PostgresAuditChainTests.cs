using Kun.Audit;
using Kun.Audit.DependencyInjection;
using Kun.Audit.EntityFrameworkCore;
using Kun.Audit.EntityFrameworkCore.DependencyInjection;
using Kun.IntegrationTests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Kun.IntegrationTests.Auditing;

public sealed class PostgresAuditChainTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _postgres;
    public PostgresAuditChainTests(PostgresFixture postgres) => _postgres = postgres;

    [Fact]
    public async Task TwoIndependentReplicas_AppendThousandEntries_OneReplayableChain()
    {
        await using var sourceA = NpgsqlDataSource.Create(_postgres.ConnectionString);
        await using var sourceB = NpgsqlDataSource.Create(_postgres.ConnectionString);
        var schema = "chain_" + Guid.NewGuid().ToString("N");
        var servicesA = new ServiceCollection();
        var servicesB = new ServiceCollection();
        servicesA.AddKunAudit().UsePostgresChain(sourceA, schema);
        servicesB.AddKunAudit().UsePostgresChain(sourceB, schema);
        await using var replicaA = servicesA.BuildServiceProvider();
        await using var replicaB = servicesB.BuildServiceProvider();
        var a = replicaA.GetRequiredService<PostgresAuditChain>();
        var b = replicaB.GetRequiredService<PostgresAuditChain>();
        Assert.Same(a, replicaA.GetRequiredService<IAuditChainState>());
        Assert.Same(a, replicaA.GetRequiredService<IAuditSink>());
        Assert.NotSame(a, b);
        await a.EnsureCreatedAsync(TestContext.Current.CancellationToken);

        async Task Produce(PostgresAuditChain replica)
        {
            for (var i = 0; i < 500; i++)
                await Append(replica, "tenant-a");
        }
        await Task.WhenAll(Produce(a), Produce(b));
        var entries = new List<AuditEntry>();
        await foreach (var entry in b.ReadAsync("tenant-a", TestContext.Current.CancellationToken)) entries.Add(entry);
        Assert.Equal(1000, entries.Count);
        Assert.Equal(1000, entries.Select(e => e.ContentHash).Distinct().Count());
        string? previous = null;
        foreach (var entry in entries)
        {
            Assert.Equal(previous, entry.PrevHash);
            Assert.Equal(AuditContentHasher.Compute(entry), entry.ContentHash);
            Assert.Equal(AuditActorType.Agent, entry.ActorType);
            previous = entry.ContentHash;
        }
        await using var command = sourceA.CreateCommand($"SELECT tip_seq, tip_hash FROM \"{schema}\".audit_chain_state WHERE tenant_key = 'tenant:tenant-a'");
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        Assert.True(await reader.ReadAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1000, reader.GetInt64(0));
        Assert.Equal(previous, reader.GetString(1));
    }

    [Fact]
    public async Task Append_FailureAfterWrite_RollsBackEntryAndTip()
    {
        await using var source = NpgsqlDataSource.Create(_postgres.ConnectionString);
        var chain = new PostgresAuditChain(source, "chain_" + Guid.NewGuid().ToString("N"));
        await chain.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        var first = await Append(chain, "tenant-a");
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await chain.AppendAsync("tenant-a", async previous =>
        {
            var entry = Create("tenant-a", previous);
            await chain.WriteAsync(entry, TestContext.Current.CancellationToken);
            throw new InvalidOperationException("写入后、提交前故障");
        }, TestContext.Current.CancellationToken));
        var second = await Append(chain, "tenant-a");
        var rows = new List<AuditEntry>();
        await foreach (var row in chain.ReadAsync("tenant-a", TestContext.Current.CancellationToken)) rows.Add(row);
        Assert.Equal(2, rows.Count);
        Assert.Equal(first, rows[1].PrevHash);
        Assert.Equal(second, rows[1].ContentHash);
        Assert.NotEqual(rows[1].ContentHash, AuditContentHasher.Compute(rows[1] with { ActorType = AuditActorType.User }));
    }

    [Fact]
    public async Task GlobalAndTenantChains_AreIndependent_AndMismatchedSinkWriteIsRejected()
    {
        await using var source = NpgsqlDataSource.Create(_postgres.ConnectionString);
        var chain = new PostgresAuditChain(source, "chain_" + Guid.NewGuid().ToString("N"));
        await chain.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        await Append(chain, null);
        await Append(chain, "__global__");
        await foreach (var row in chain.ReadAsync("__global__", TestContext.Current.CancellationToken)) Assert.Null(row.PrevHash);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await chain.AppendAsync("tenant-a", async previous =>
        {
            var wrongTenant = Create("tenant-b", previous);
            await chain.WriteAsync(wrongTenant, TestContext.Current.CancellationToken);
            return wrongTenant.ContentHash;
        }, TestContext.Current.CancellationToken));
        var empty = new List<AuditEntry>();
        await foreach (var row in chain.ReadAsync("tenant-a", TestContext.Current.CancellationToken)) empty.Add(row);
        Assert.Empty(empty);
    }

    private static ValueTask<string> Append(PostgresAuditChain chain, string? tenant) =>
        chain.AppendAsync(tenant, async previous =>
        {
            var entry = Create(tenant, previous);
            await chain.WriteAsync(entry, TestContext.Current.CancellationToken);
            return entry.ContentHash;
        }, TestContext.Current.CancellationToken);

    private static AuditEntry Create(string? tenant, string? previous)
    {
        // 末位 tick 故意不与 PostgreSQL 微秒对齐，验证持久化后仍可逐字重放哈希。
        var time = new DateTimeOffset(2026, 10, 9, 0, 0, 0, TimeSpan.Zero).AddTicks(7);
        var entry = new AuditEntry(Guid.CreateVersion7(), tenant, "agent-1", "CreateOrder", time,
            10, AuditOutcome.Success, null, previous, "")
        { ActorType = AuditActorType.Agent };
        return entry with { ContentHash = AuditContentHasher.Compute(entry) };
    }
}
