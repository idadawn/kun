using Amazon.S3;
using FluentAssertions;
using Kun.Storage.Abstractions;
using Kun.Storage.DependencyInjection;
using Kun.Storage.Implementations;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Kun.Storage.Tests;

public sealed class KunStorageServiceCollectionExtensionsTests
{
    [Fact]
    public void AddLocalStorage_Resolves_LocalFileStorage()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLocalStorage(o => o.RootPath = "./test-storage");

        using var sp = services.BuildServiceProvider();
        var fs = sp.GetRequiredService<IFileStorage>();

        fs.Should().BeOfType<LocalFileStorage>();
    }

    [Fact]
    public void AddS3Storage_Resolves_S3FileStorage()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddS3Storage(o =>
        {
            o.BucketName = "test-bucket";
            o.Endpoint = "http://localhost:9000";
            o.AccessKey = "test";
            o.SecretKey = "test-secret";
            o.Region = "us-east-1";
        });

        using var sp = services.BuildServiceProvider();
        var fs = sp.GetRequiredService<IFileStorage>();

        fs.Should().BeOfType<S3FileStorage>();
    }

    [Fact]
    public void AddS3Storage_Registers_IAmazonS3_As_Singleton()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddS3Storage(o =>
        {
            o.BucketName = "b";
            o.Endpoint = "http://localhost:9000";
            o.AccessKey = "k";
            o.SecretKey = "s";
        });

        using var sp = services.BuildServiceProvider();
        var a = sp.GetRequiredService<IAmazonS3>();
        var b = sp.GetRequiredService<IAmazonS3>();
        a.Should().BeSameAs(b);
    }

    [Fact]
    public void AddKunStorage_Throws_For_Unknown_Provider()
    {
        var services = new ServiceCollection();

        var act = () => services.AddKunStorage(o => o.Provider = (StorageProvider)99);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
