using Amazon.Runtime;
using Amazon.S3;
using Kun.Storage.Abstractions;
using Kun.Storage.Configuration;
using Kun.Storage.Implementations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Kun.Storage.DependencyInjection;

/// <summary>Service-collection extensions that register <c>Kun.Storage</c> backends.</summary>
public static class KunStorageServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Kun.Storage abstraction surface. Call
    /// <see cref="UseS3"/> or <see cref="UseLocal"/> on the returned
    /// builder to pick a backend.
    /// </summary>
    public static KunStorageBuilder AddKunStorage(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return new KunStorageBuilder(services);
    }

    /// <summary>
    /// Registers <see cref="LocalFileSystemStorage"/> as the active
    /// <see cref="IFileStorage"/>. Intended for development and tests;
    /// presigned URLs are not supported.
    /// </summary>
    public static KunStorageBuilder UseLocal(
        this KunStorageBuilder builder,
        Action<LocalStorageOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (configure is not null)
        {
            builder.Services.Configure(configure);
        }
        else
        {
            builder.Services.AddOptions<LocalStorageOptions>();
        }
        builder.Services.RemoveAll<IFileStorage>();
        builder.Services.AddSingleton<IFileStorage, LocalFileSystemStorage>();
        return builder;
    }

    /// <summary>
    /// Registers <see cref="S3FileStorage"/> as the active
    /// <see cref="IFileStorage"/>. Also registers a singleton
    /// <see cref="IAmazonS3"/> client built from the supplied options —
    /// callers that need to share the client (e.g. with another bucket)
    /// should register it themselves before calling this method and it
    /// will not be overwritten.
    /// </summary>
    public static KunStorageBuilder UseS3(
        this KunStorageBuilder builder,
        Action<S3StorageOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        builder.Services.Configure(configure);
        builder.Services.TryAddSingleton<IAmazonS3>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<S3StorageOptions>>().Value;
            var config = new AmazonS3Config { ForcePathStyle = opts.ForcePathStyle };
            if (!string.IsNullOrWhiteSpace(opts.Endpoint))
            {
                config.ServiceURL = opts.Endpoint;
            }
            if (!string.IsNullOrWhiteSpace(opts.Region))
            {
                config.RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(opts.Region);
            }
            var credentials = new BasicAWSCredentials(opts.AccessKey, opts.SecretKey);
            return new AmazonS3Client(credentials, config);
        });
        builder.Services.RemoveAll<IFileStorage>();
        builder.Services.AddSingleton<IFileStorage, S3FileStorage>();
        return builder;
    }
}
