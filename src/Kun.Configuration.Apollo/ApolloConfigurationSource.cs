using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Kun.Configuration.Apollo;

/// <summary>Apollo 标准 IConfiguration 源。</summary>
public sealed class ApolloConfigurationSource(ApolloOptions options, HttpClient? client = null, ILogger? logger = null) : IConfigurationSource
{
    /// <inheritdoc />
    public IConfigurationProvider Build(IConfigurationBuilder builder) => new ApolloConfigurationProvider(options, client, logger);
}
/// <summary>显式添加 Apollo；源顺序由宿主按 appsettings、Apollo、Secret、环境变量、命令行安排。</summary>
public static class ApolloConfigurationExtensions
{
    /// <summary>添加平台级 Apollo 配置源，HttpClient 由传入方管理或由 provider 自建。</summary>
    public static IConfigurationBuilder AddApollo(this IConfigurationBuilder builder, ApolloOptions options,
        HttpClient? client = null, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.Add(new ApolloConfigurationSource(options, client, logger));
    }
}
