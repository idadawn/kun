using Kun.EventBus.Background;
using Xunit;

namespace Kun.EventBus.Tests.Background;

public sealed class NullOutboxLockProviderTests
{
    [Fact]
    public async Task TryAcquireAsync_AlwaysReturnsNonNullDisposable()
    {
        var provider = NullOutboxLockProvider.Instance;

        var lease = await provider.TryAcquireAsync(
            "outbox-publisher",
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);

        Assert.NotNull(lease);
        await lease.DisposeAsync();
    }

    [Fact]
    public async Task TryAcquireAsync_MultipleCallsAllSucceed()
    {
        var provider = NullOutboxLockProvider.Instance;

        var lease1 = await provider.TryAcquireAsync(
            "outbox-publisher",
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);
        var lease2 = await provider.TryAcquireAsync(
            "outbox-publisher",
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);

        Assert.NotNull(lease1);
        Assert.NotNull(lease2);

        await lease1!.DisposeAsync();
        await lease2!.DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsync_IsIdempotent()
    {
        var provider = NullOutboxLockProvider.Instance;
        var lease = await provider.TryAcquireAsync(
            "outbox-publisher",
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);

        Assert.NotNull(lease);
        // Dispose twice — must not throw.
        await lease!.DisposeAsync();
        await lease.DisposeAsync();
    }
}
