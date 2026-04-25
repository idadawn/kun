using Kun.Gateway.RateLimiting;
using Kun.Gateway.RateLimiting.Resolvers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Kun.Gateway.Tests;

public sealed class KunSlidingWindowRateLimiterTests
{
    [Fact]
    public void AddKunSlidingWindowRateLimiter_NullServices_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            ((IServiceCollection)null!).AddKunSlidingWindowRateLimiter());
    }

    [Fact]
    public void AddKunSlidingWindowRateLimiter_NoConfigure_UsesDefaults()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddKunSlidingWindowRateLimiter();

        using var sp = services.BuildServiceProvider();
        var opts = sp.GetRequiredService<IOptions<KunSlidingWindowOptions>>().Value;
        Assert.Equal(100, opts.PermitLimit);
        Assert.Equal(TimeSpan.FromMinutes(1), opts.Window);
        Assert.Equal(6, opts.SegmentsPerWindow);
        Assert.Equal(0, opts.QueueLimit);
    }

    [Fact]
    public void AddKunSlidingWindowRateLimiter_ConfigureKun_AppliesOverrides()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddKunSlidingWindowRateLimiter(o =>
        {
            o.PermitLimit = 42;
            o.Window = TimeSpan.FromSeconds(30);
            o.SegmentsPerWindow = 3;
            o.QueueLimit = 7;
        });

        using var sp = services.BuildServiceProvider();
        var opts = sp.GetRequiredService<IOptions<KunSlidingWindowOptions>>().Value;
        Assert.Equal(42, opts.PermitLimit);
        Assert.Equal(TimeSpan.FromSeconds(30), opts.Window);
        Assert.Equal(3, opts.SegmentsPerWindow);
        Assert.Equal(7, opts.QueueLimit);
    }

    [Fact]
    public void AddKunSlidingWindowRateLimiter_RegistersDefaultPartitionResolver()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddKunSlidingWindowRateLimiter();

        using var sp = services.BuildServiceProvider();
        Assert.IsType<TenantRateLimitPartitionResolver>(sp.GetService<IRateLimitPartitionResolver>());
    }

    [Fact]
    public void AddKunSlidingWindowRateLimiter_PreservesConsumerResolver()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IRateLimitPartitionResolver, IpRateLimitPartitionResolver>();

        services.AddKunSlidingWindowRateLimiter();

        using var sp = services.BuildServiceProvider();
        Assert.IsType<IpRateLimitPartitionResolver>(sp.GetService<IRateLimitPartitionResolver>());
    }
}
