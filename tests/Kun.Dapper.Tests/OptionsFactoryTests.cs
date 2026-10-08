using Kun.Dapper.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Kun.Dapper.Tests;

public sealed class OptionsFactoryTests
{
    [Fact]
    public void FactoryOverload_ConfiguresInitOnlyConnectionString()
    {
        var services = new ServiceCollection();
        services.AddKunDapperNpgsql(() => new ReadDbOptions { ConnectionString = "Host=localhost;Database=read" });
        using var provider = services.BuildServiceProvider();
        Assert.Equal("Host=localhost;Database=read", provider.GetRequiredService<ReadDbOptions>().ConnectionString);
        Assert.NotNull(provider.GetRequiredService<IReadDbConnectionFactory>());
    }
}
