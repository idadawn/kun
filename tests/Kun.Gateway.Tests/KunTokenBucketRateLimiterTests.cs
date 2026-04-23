using Kun.Gateway.RateLimiting;
using Kun.Gateway.RateLimiting.Resolvers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Kun.Gateway.Tests;

public sealed class KunTokenBucketRateLimiterTests
{
    [Fact]
    public void AddKunTokenBucketRateLimiter_NullServices_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            ((IServiceCollection)null!).AddKunTokenBucketRateLimiter());
    }

    [Fact]
    public void AddKunTokenBucketRateLimiter_NoConfigure_UsesDefaults()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddKunTokenBucketRateLimiter();

        using var sp = services.BuildServiceProvider();
        var opts = sp.GetRequiredService<IOptions<KunTokenBucketOptions>>().Value;
        Assert.Equal(100, opts.TokenLimit);
        Assert.Equal(20, opts.TokensPerPeriod);
        Assert.Equal(TimeSpan.FromSeconds(10), opts.ReplenishmentPeriod);
        Assert.True(opts.AutoReplenishment);
        Assert.Equal(0, opts.QueueLimit);
    }

    [Fact]
    public void AddKunTokenBucketRateLimiter_ConfigureKun_AppliesOverrides()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddKunTokenBucketRateLimiter(o =>
        {
            o.TokenLimit = 500;
            o.TokensPerPeriod = 50;
            o.ReplenishmentPeriod = TimeSpan.FromSeconds(5);
            o.AutoReplenishment = false;
            o.QueueLimit = 9;
        });

        using var sp = services.BuildServiceProvider();
        var opts = sp.GetRequiredService<IOptions<KunTokenBucketOptions>>().Value;
        Assert.Equal(500, opts.TokenLimit);
        Assert.Equal(50, opts.TokensPerPeriod);
        Assert.Equal(TimeSpan.FromSeconds(5), opts.ReplenishmentPeriod);
        Assert.False(opts.AutoReplenishment);
        Assert.Equal(9, opts.QueueLimit);
    }

    [Fact]
    public void AddKunTokenBucketRateLimiter_RegistersDefaultPartitionResolver()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddKunTokenBucketRateLimiter();

        using var sp = services.BuildServiceProvider();
        Assert.IsType<TenantRateLimitPartitionResolver>(sp.GetService<IRateLimitPartitionResolver>());
    }
}
