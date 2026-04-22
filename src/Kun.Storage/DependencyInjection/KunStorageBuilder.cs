using Microsoft.Extensions.DependencyInjection;

namespace Kun.Storage.DependencyInjection;

/// <summary>
/// Fluent builder returned from <see cref="KunStorageServiceCollectionExtensions.AddKunStorage"/>.
/// Call <c>UseS3(...)</c> or <c>UseLocal(...)</c> exactly once to pick a
/// backend; the last call wins so misconfigured chains can be detected
/// at startup with a single <see cref="IServiceProvider"/> resolve.
/// </summary>
public sealed class KunStorageBuilder
{
    /// <summary>Creates a builder bound to <paramref name="services"/>.</summary>
    public KunStorageBuilder(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Services = services;
    }

    /// <summary>The service collection being configured.</summary>
    public IServiceCollection Services { get; }
}
