using Kun.Dapper.Npgsql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kun.Dapper.DependencyInjection;

/// <summary>Registers Kun.Dapper services into the DI container.</summary>
public static class KunDapperServiceCollectionExtensions
{
    /// <summary>Adds the read-side connection factory and Dapper defaults.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Callback to configure <see cref="ReadDbOptions"/>.</param>
    /// <remarks>
    /// Use this overload when you supply your own <see cref="IReadDbConnectionFactory"/>
    /// registration. For PostgreSQL projects, prefer <see cref="AddKunDapperNpgsql(IServiceCollection, Func{ReadDbOptions})"/>.
    /// </remarks>
    public static IServiceCollection AddKunDapper(
        this IServiceCollection services,
        Action<ReadDbOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var options = new ReadDbOptions { ConnectionString = string.Empty };
        configure(options);

        services.TryAddSingleton(options);

        return services;
    }

    /// <summary>
    /// Adds the read-side connection factory backed by Npgsql (PostgreSQL).
    /// Registers <see cref="ReadDbOptions"/> as a singleton and
    /// <see cref="NpgsqlReadDbConnectionFactory"/> as the <see cref="IReadDbConnectionFactory"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Callback to configure <see cref="ReadDbOptions"/>.</param>
    public static IServiceCollection AddKunDapperNpgsql(
        this IServiceCollection services,
        Action<ReadDbOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var options = new ReadDbOptions { ConnectionString = string.Empty };
        configure(options);

        services.TryAddSingleton(options);
        services.TryAddSingleton<IReadDbConnectionFactory, NpgsqlReadDbConnectionFactory>();

        return services;
    }
    /// <summary>通过工厂创建不可变 options，支持 init-only ConnectionString；兼容保留旧 Action 重载。</summary>
    public static IServiceCollection AddKunDapper(this IServiceCollection services, Func<ReadDbOptions> createOptions)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(createOptions);
        var options = createOptions();
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ConnectionString);
        services.TryAddSingleton(options);
        return services;
    }

    /// <summary>通过不可变 options 工厂装配 PostgreSQL 读侧连接。</summary>
    public static IServiceCollection AddKunDapperNpgsql(this IServiceCollection services, Func<ReadDbOptions> createOptions)
    {
        services.AddKunDapper(createOptions);
        services.TryAddSingleton<IReadDbConnectionFactory, NpgsqlReadDbConnectionFactory>();
        return services;
    }

}
