using FluentAssertions;
using Kun.Storage.Abstractions;
using Kun.Storage.DependencyInjection;
using Kun.Storage.Implementations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Kun.Storage.Tests;

public sealed class DependencyInjectionTests
{
    [Fact]
    public void UseLocal_RegistersLocalFileSystemStorage()
    {
        var services = RegisterLoggingStubs();

        services.AddKunStorage().UseLocal(o => o.RootPath = "/tmp/kun-di-test");

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IFileStorage>().Should().BeOfType<LocalFileSystemStorage>();
    }

    [Fact]
    public void UseS3_RegistersS3FileStorage()
    {
        var services = RegisterLoggingStubs();

        services.AddKunStorage().UseS3(o =>
        {
            o.BucketName = "test-bucket";
            o.Endpoint = "http://localhost:9000";
            o.Region = "us-east-1";
            o.AccessKey = "minio";
            o.SecretKey = "minio1234";
            o.ForcePathStyle = true;
        });

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IFileStorage>().Should().BeOfType<S3FileStorage>();
    }

    [Fact]
    public void UseLocal_AfterUseS3_ReplacesS3()
    {
        var services = RegisterLoggingStubs();

        services.AddKunStorage()
            .UseS3(o =>
            {
                o.BucketName = "test-bucket";
                o.Region = "us-east-1";
                o.AccessKey = "a";
                o.SecretKey = "b";
            })
            .UseLocal();

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IFileStorage>().Should().BeOfType<LocalFileSystemStorage>();
    }

    private static ServiceCollection RegisterLoggingStubs()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        return services;
    }
}
