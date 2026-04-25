using FluentAssertions;
using Xunit;

namespace Kun.MultiTenancy.Tests;

public sealed class AsyncLocalTenantContextTests
{
    [Fact]
    public void BeginScope_SetsAndRestoresTenantId()
    {
        var ctx = new AsyncLocalTenantContext();
        ctx.TenantId.Should().BeNull();

        using (ctx.BeginScope("tenant-1"))
        {
            ctx.TenantId.Should().Be("tenant-1");

            using (ctx.BeginScope("tenant-2"))
            {
                ctx.TenantId.Should().Be("tenant-2");
            }

            ctx.TenantId.Should().Be("tenant-1");
        }

        ctx.TenantId.Should().BeNull();
    }

    [Fact]
    public async Task TwoTenants_DoNotLeak_AcrossConcurrentTasks()
    {
        var ctx = new AsyncLocalTenantContext();
        var observed = new System.Collections.Concurrent.ConcurrentBag<(string Tenant, string Observed)>();

        async Task ObserveAsync(string tenant)
        {
            using (ctx.BeginScope(tenant))
            {
                // Yield to let other tasks run on the same logical thread pool.
                await Task.Yield();
                await Task.Delay(Random.Shared.Next(1, 20), TestContext.Current.CancellationToken);
                observed.Add((tenant, ctx.TenantId ?? "<null>"));
            }
        }

        var tasks = Enumerable.Range(0, 128)
            .Select(i => ObserveAsync($"tenant-{i}"))
            .ToArray();
        await Task.WhenAll(tasks);

        observed.Should().OnlyContain(entry => entry.Tenant == entry.Observed,
            "AsyncLocal isolates tenant scope across concurrent async flows");
    }
}
