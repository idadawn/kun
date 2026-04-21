using Amazon.Runtime;
using Amazon.S3;
using Kun.Storage.Abstractions;
using Kun.Storage.Implementations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kun.Storage.DependencyInjection;

/// <summary>
/// Extension methods to register Kun.Storage services.
/// </summary>
public static class KunStorageServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IFileStorage"/> and related services based on <see cref="StorageOptions"/>.
    /// </summary>
    public static IServiceCollection AddKunStorage(
        this IServiceCollection services,
        Action<StorageOptions>? configure = null)
    {
        var options = new StorageOptions();
        configure?.Invoke(options);

        services.Configure<StorageOptions>(o =>
        {
            o.Provider = options.Provider;
            o.RootPath = options.RootPath;
            o.BucketName = options.BucketName;
            o.Endpoint = options.Endpoint;
            o.AccessKey = options.AccessKey;
            o.SecretKey = options.SecretKey;
            o.Region = options.Region;
        });

        if (options.Provider == StorageProvider.S3)
        {
            services.TryAddSingleton<IAmazonS3>(_ =>
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
            });

            services.TryAddSingleton<IFileStorage, S3FileStorage>();
        }
        else
        {
            services.TryAddSingleton<IFileStorage, LocalFileStorage>();
        }

        return services;
    }
}
