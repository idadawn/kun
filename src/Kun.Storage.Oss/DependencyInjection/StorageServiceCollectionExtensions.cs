using Aliyun.OSS;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Kun.Storage.Oss.DependencyInjection;

/// <summary>DI extension methods for registering <see cref="OssFileStorage"/>.</summary>
public static class StorageServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="OssFileStorage"/> as the <see cref="IFileStorage"/> implementation
    /// and configures an <see cref="IOss"/> client from <see cref="OssStorageOptions"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Configures <see cref="OssStorageOptions"/>.</param>
    public static IServiceCollection AddKunOssFileStorage(
        this IServiceCollection services,
        Action<OssStorageOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.Configure(configure);

        services.TryAddSingleton<IOss>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<OssStorageOptions>>().Value;

            if (string.IsNullOrWhiteSpace(opts.Endpoint))
            {
                throw new InvalidOperationException("OssStorageOptions.Endpoint must be set.");
            }

            if (string.IsNullOrWhiteSpace(opts.AccessKeyId) || string.IsNullOrWhiteSpace(opts.AccessKeySecret))
            {
                throw new InvalidOperationException(
                    "OssStorageOptions.AccessKeyId and AccessKeySecret must be set " +
                    "(OSS has no ambient credential resolution chain).");
            }

            return new OssClient(opts.Endpoint, opts.AccessKeyId, opts.AccessKeySecret);
        });

        services.TryAddSingleton<IFileStorage, OssFileStorage>();
        return services;
    }
}
