using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization.Metadata;
using Kun.Configuration.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kun.Configuration.Extensions;

/// <summary>
/// Service-collection extensions that register Kun typed-options
/// validation.
/// </summary>
public static class KunConfigurationServiceCollectionExtensions
{
    /// <summary>
    /// Registers <typeparamref name="TOptions"/> bound to
    /// <paramref name="section"/>, validates DataAnnotations
    /// attributes at host start, and fails fast with
    /// section-qualified messages.
    /// </summary>
    /// <typeparam name="TOptions">
    /// Options type; must be a reference type. Annotated with
    /// <see cref="DynamicallyAccessedMemberTypes.All"/> so the
    /// trimmer preserves the properties DataAnnotations walks.
    /// </typeparam>
    /// <param name="services">Service collection to register into.</param>
    /// <param name="section">Configuration section path (e.g. <c>"Authentik"</c>, <c>"Database:Primary"</c>).</param>
    /// <returns>The underlying <see cref="OptionsBuilder{TOptions}"/> for further chaining.</returns>
    [RequiresUnreferencedCode("DataAnnotations-based options validation walks properties by reflection.")]
    [RequiresDynamicCode("DataAnnotations-based options validation walks properties by reflection.")]
    [UnconditionalSuppressMessage("ConfigurationBinder", "SYSLIB1100",
        Justification = "Consumer opts into reflection-based binding by calling this annotated entry point.")]
    [UnconditionalSuppressMessage("ConfigurationBinder", "SYSLIB1101",
        Justification = "Consumer opts into reflection-based binding by calling this annotated entry point.")]
    [UnconditionalSuppressMessage("ConfigurationBinder", "SYSLIB1104",
        Justification = "Consumer opts into reflection-based binding by calling this annotated entry point.")]
    public static OptionsBuilder<TOptions> AddKunOptions<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TOptions>(
        this IServiceCollection services,
        string section)
        where TOptions : class
    {
        return services.AddKunOptions<TOptions>(section, critical: true);
    }
    /// <summary>关键配置在启动时校验；可选配置在首次读取时校验，未使用时不阻断宿主启动。</summary>
    [RequiresUnreferencedCode("配置绑定与 DataAnnotations 校验需要反射。")]
    [RequiresDynamicCode("配置绑定与 DataAnnotations 校验需要反射。")]
    public static OptionsBuilder<TOptions> AddKunOptions<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TOptions>(
        this IServiceCollection services, string section, bool critical) where TOptions : class
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(section);

        services.AddSingleton<IValidateOptions<TOptions>>(sp =>
        {
            var validators = sp.GetServices<Abstractions.IKunConfigurationValidator<TOptions>>();
            return new KunOptionsValidator<TOptions>(section, validators);
        });

        services.AddLogging();
        services.AddSingleton<IOptionsMonitor<TOptions>>(sp => new LastGoodOptionsMonitor<TOptions>(
            sp.GetRequiredService<IOptionsFactory<TOptions>>(), sp.GetServices<IOptionsChangeTokenSource<TOptions>>(),
            sp.GetRequiredService<ILogger<LastGoodOptionsMonitor<TOptions>>>()).Start());

        var builder = services.AddOptions<TOptions>().BindConfiguration(section);
        return critical ? builder.ValidateOnStart() : builder;
    }

    /// <summary>绑定并校验配置，同时登记基于源生成 JSON 元数据的 Schema 2020-12。</summary>
    [RequiresUnreferencedCode("与既有 AddKunOptions 相同，绑定及 DataAnnotations 需要反射。Schema 导出本身使用源生成元数据。")]
    [RequiresDynamicCode("与既有 AddKunOptions 相同，调用方显式选择反射绑定。")]
    public static OptionsBuilder<TOptions> AddKunOptions<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TOptions>(
        this IServiceCollection services, string section, JsonTypeInfo<TOptions> typeInfo, bool critical = true) where TOptions : class
    {
        ArgumentNullException.ThrowIfNull(typeInfo);
        var builder = services.AddKunOptions<TOptions>(section, critical);
        services.AddSingleton(new OptionsSchema<TOptions>(section, typeInfo));
        return builder;
    }

}
