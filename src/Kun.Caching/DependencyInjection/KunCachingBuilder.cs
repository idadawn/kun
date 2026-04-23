using Microsoft.Extensions.DependencyInjection;

namespace Kun.Caching.DependencyInjection;

/// <summary>Fluent builder returned from <c>AddKunCaching</c>.</summary>
public sealed class KunCachingBuilder
{
    /// <summary>Creates a builder bound to <paramref name="services"/>.</summary>
    public KunCachingBuilder(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Services = services;
    }

    /// <summary>The service collection being configured.</summary>
    public IServiceCollection Services { get; }
}
