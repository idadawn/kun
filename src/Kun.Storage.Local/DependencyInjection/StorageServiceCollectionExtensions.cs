using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kun.Storage.Local.DependencyInjection;

/// <summary>DI extension methods for registering <see cref="LocalFileSystemStorage"/>.</summary>
public static class StorageServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="LocalFileSystemStorage"/> as the <see cref="IFileStorage"/> implementation.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Configures <see cref="LocalFileStorageOptions"/>.</param>
    public static IServiceCollection AddKunLocalFileStorage(
        this IServiceCollection services,
        Action<LocalFileStorageOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.Configure(configure);
        services.TryAddSingleton<IFileStorage, LocalFileSystemStorage>();
        return services;
    }
}
