using FluentAssertions;
using Kun.Audit;
using Xunit;

namespace Kun.Audit.Tests;

public sealed class InMemoryAuditChainStateTests
{
    [Fact]
    public async Task AppendAsync_First_Call_Passes_Null_Prev()
    {
        using var chain = new InMemoryAuditChainState();

        string? observedPrev = "sentinel";
        await chain.AppendAsync("t", prev =>
        {
            observedPrev = prev;
            return ValueTask.FromResult("hash1");
        }, TestContext.Current.CancellationToken);

        observedPrev.Should().BeNull();
    }

    [Fact]
    public async Task AppendAsync_Second_Call_Passes_Previous_Hash()
    {
        using var chain = new InMemoryAuditChainState();

        await chain.AppendAsync("t", _ => ValueTask.FromResult("h1"),
            TestContext.Current.CancellationToken);

        string? observedPrev = null;
        await chain.AppendAsync("t", prev =>
        {
            observedPrev = prev;
            return ValueTask.FromResult("h2");
        }, TestContext.Current.CancellationToken);

        observedPrev.Should().Be("h1");
    }

    [Fact]
    public async Task AppendAsync_Different_Tenants_Have_Independent_Chains()
    {
        using var chain = new InMemoryAuditChainState();

        await chain.AppendAsync("a", _ => ValueTask.FromResult("a1"),
            TestContext.Current.CancellationToken);
        await chain.AppendAsync("b", _ => ValueTask.FromResult("b1"),
            TestContext.Current.CancellationToken);

        chain.PeekTip("a").Should().Be("a1");
        chain.PeekTip("b").Should().Be("b1");
        chain.PeekTip("c").Should().BeNull();
    }

    [Fact]
    public async Task AppendAsync_Concurrent_Same_Tenant_Serialises_And_Chains_Correctly()
    {
        // Fire 100 concurrent appends on one tenant. Each produces a new hash based on the
        // prev hash it observed inside its factory. If the state serialises properly, the
        // sequence of (prev,new) pairs forms a linked list: prev_N == new_{N-1}, and no
        // two factories observe the same prev.
        using var chain = new InMemoryAuditChainState();
        var observed = new System.Collections.Concurrent.ConcurrentBag<(string? Prev, string Next)>();

        var ct = TestContext.Current.CancellationToken;
        var tasks = Enumerable.Range(0, 100).Select(i =>
            chain.AppendAsync("t", async prev =>
            {
                // Tiny yield to increase interleaving pressure.
                await Task.Yield();
                var next = $"h{i:D3}-{Guid.NewGuid():N}";
                observed.Add((prev, next));
                return next;
            }, ct).AsTask()).ToArray();

        await Task.WhenAll(tasks);

        observed.Should().HaveCount(100);
        // Build set of "next" values — every non-null prev must appear as some "next" value.
        var nextValues = observed.Select(p => p.Next).ToHashSet();
        var prevValues = observed.Where(p => p.Prev is not null).Select(p => p.Prev!).ToArray();

        prevValues.Should().OnlyContain(p => nextValues.Contains(p),
            "every observed prev must match a previous append's next");

        // Exactly one append saw null prev (the very first one into the chain).
        observed.Count(p => p.Prev is null).Should().Be(1);

        // All distinct: no two factories got the same prev value.
        prevValues.Distinct().Count().Should().Be(prevValues.Length,
            "two concurrent writes must not observe the same prev hash");
    }

    [Fact]
    public async Task AppendAsync_After_Dispose_Throws()
    {
        var chain = new InMemoryAuditChainState();
        chain.Dispose();

        Func<Task> act = async () =>
            await chain.AppendAsync("t", _ => ValueTask.FromResult("h"),
                TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task AppendAsync_Null_Factory_Throws()
    {
        using var chain = new InMemoryAuditChainState();

        Func<Task> act = async () =>
            await chain.AppendAsync("t", computeNewHash: null!,
                TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task AppendAsync_Factory_Returning_Empty_Hash_Throws()
    {
        using var chain = new InMemoryAuditChainState();

        Func<Task> act = async () =>
            await chain.AppendAsync("t", _ => ValueTask.FromResult(""),
                TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ArgumentException>();
    }
}
