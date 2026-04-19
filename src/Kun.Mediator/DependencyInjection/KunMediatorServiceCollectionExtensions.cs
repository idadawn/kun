using Microsoft.Extensions.DependencyInjection;

namespace Kun.Mediator.DependencyInjection;

/// <summary>
/// Extension methods for registering <see cref="KunMediator"/> with an
/// <see cref="IServiceCollection"/>.
/// </summary>
public static class KunMediatorServiceCollectionExtensions
{
    /// <summary>
    /// Adds <see cref="KunMediator"/> and all handlers/behaviors configured via
    /// <paramref name="configure"/> to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">
    /// A delegate that configures the <see cref="KunMediatorBuilder"/> with request handlers,
    /// pipeline behaviors, and notification handlers.
    /// </param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddKunMediator(
        this IServiceCollection services,
        Action<KunMediatorBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var builder = new KunMediatorBuilder(services);
        configure(builder);

        services.AddSingleton<KunMediator>(sp =>
            builder.Build(sp.GetRequiredService<IServiceScopeFactory>()));
        services.AddSingleton<ISender>(sp => sp.GetRequiredService<KunMediator>());
        services.AddSingleton<IPublisher>(sp => sp.GetRequiredService<KunMediator>());
        return services;
    }
}
