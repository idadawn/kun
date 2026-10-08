#pragma warning disable KUNLEGACY001 // 旧接口的兼容实现，不作为新代码的入口。
using Amazon.Runtime;
using Amazon.S3;
using Kun.Storage.Abstractions;
using Kun.Storage.Implementations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kun.Storage.DependencyInjection;

/// <summary>
/// Registration extensions for Kun.Storage.
/// </summary>
public static class KunStorageServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IFileStorage"/> using the provider specified in
    /// <see cref="StorageOptions.Provider"/>. Prefer <see cref="AddS3Storage"/>
    /// or <see cref="AddLocalStorage"/> for a clearer call site.
    /// </summary>
    public static IServiceCollection AddKunStorage(
        this IServiceCollection services,
        Action<StorageOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.Configure(configure);

        var snapshot = new StorageOptions();
        configure(snapshot);

        return snapshot.Provider switch
        {
            StorageProvider.S3 => services.AddS3Infrastructure(snapshot),
            StorageProvider.Local => services.AddLocalInfrastructure(),
            _ => throw new ArgumentOutOfRangeException(
                nameof(configure),
                snapshot.Provider,
                "Unknown StorageProvider."),
        };
    }

    /// <summary>Registers <see cref="LocalFileStorage"/> as the <see cref="IFileStorage"/>.</summary>
    public static IServiceCollection AddLocalStorage(
        this IServiceCollection services,
        Action<StorageOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services.AddKunStorage(options =>
        {
            options.Provider = StorageProvider.Local;
            configure?.Invoke(options);
        });
    }

    /// <summary>Registers <see cref="S3FileStorage"/> as the <see cref="IFileStorage"/>.</summary>
    public static IServiceCollection AddS3Storage(
        this IServiceCollection services,
        Action<StorageOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);
        return services.AddKunStorage(options =>
        {
            configure(options);
            options.Provider = StorageProvider.S3;
        });
    }

    private static IServiceCollection AddS3Infrastructure(
        this IServiceCollection services,
        StorageOptions snapshot)
    {
        services.TryAddSingleton<IAmazonS3>(_ => BuildS3Client(snapshot));
        services.TryAddSingleton<IFileStorage, S3FileStorage>();
        return services;
    }

    private static IServiceCollection AddLocalInfrastructure(this IServiceCollection services)
    {
        services.TryAddSingleton<IFileStorage, LocalFileStorage>();
        return services;
    }

    private static AmazonS3Client BuildS3Client(StorageOptions options)
    {
        var config = new AmazonS3Config();

        if (!string.IsNullOrWhiteSpace(options.Endpoint))
        {
            config.ServiceURL = options.Endpoint;
            config.ForcePathStyle = true;
        }

        if (!string.IsNullOrWhiteSpace(options.Region))
        {
            config.RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(options.Region);
        }

        var credentials = new BasicAWSCredentials(options.AccessKey, options.SecretKey);
        return new AmazonS3Client(credentials, config);
    }
}
