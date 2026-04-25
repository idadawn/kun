using Amazon.Runtime;
using Amazon.S3;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Kun.Storage.S3.DependencyInjection;

/// <summary>DI extension methods for registering <see cref="S3FileStorage"/>.</summary>
public static class StorageServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="S3FileStorage"/> as the <see cref="IFileStorage"/> implementation
    /// and configures an <see cref="IAmazonS3"/> client from <see cref="S3StorageOptions"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Configures <see cref="S3StorageOptions"/>.</param>
    public static IServiceCollection AddKunS3FileStorage(
        this IServiceCollection services,
        Action<S3StorageOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.Configure(configure);

        services.TryAddSingleton<IAmazonS3>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<S3StorageOptions>>().Value;

            var config = new AmazonS3Config
            {
                ForcePathStyle = opts.ForcePathStyle,
            };

            if (opts.ServiceUrl is not null)
            {
                config.ServiceURL = opts.ServiceUrl.AbsoluteUri;
            }

            if (!string.IsNullOrWhiteSpace(opts.Region))
            {
                config.AuthenticationRegion = opts.Region;
            }

            if (!string.IsNullOrWhiteSpace(opts.AccessKey) && !string.IsNullOrWhiteSpace(opts.SecretKey))
            {
                var credentials = new BasicAWSCredentials(opts.AccessKey, opts.SecretKey);
                return new AmazonS3Client(credentials, config);
            }

            return new AmazonS3Client(config);
        });

        services.TryAddSingleton<IFileStorage, S3FileStorage>();
        return services;
    }
}
