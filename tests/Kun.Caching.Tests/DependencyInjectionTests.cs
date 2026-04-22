using FluentAssertions;
using Kun.Caching.Abstractions;
using Kun.Caching.DependencyInjection;
using Kun.Caching.Implementations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Kun.Caching.Tests;

public sealed class DependencyInjectionTests
{
    [Fact]
    public void UseMemory_RegistersMemoryKunCache()
    {
        var services = BuildServices();
        services.AddKunCaching().UseMemory();

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IKunCache>().Should().BeOfType<MemoryKunCache>();
    }

    [Fact]
    public void UseRedis_RegistersRedisKunCache()
    {
        var services = BuildServices();
        services.AddKunCaching().UseRedis("localhost:6379");

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IKunCache>().Should().BeOfType<RedisKunCache>();
    }

    [Fact]
    public void DefaultSerializer_IsSystemTextJson()
    {
        var services = BuildServices();
        services.AddKunCaching().UseMemory();

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IKunCacheSerializer>().Should().BeOfType<SystemTextJsonCacheSerializer>();
    }

    [Fact]
    public void UseMemory_AfterUseRedis_ReplacesRedis()
    {
        var services = BuildServices();
        services.AddKunCaching().UseRedis("localhost:6379").UseMemory();

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IKunCache>().Should().BeOfType<MemoryKunCache>();
    }

    private static ServiceCollection BuildServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        return services;
    }
}
