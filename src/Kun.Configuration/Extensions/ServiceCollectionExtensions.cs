using System.Diagnostics.CodeAnalysis;
using Kun.Configuration.Internal;
using Microsoft.Extensions.DependencyInjection;
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
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(section);

        services.AddSingleton<IValidateOptions<TOptions>>(sp =>
        {
            var validators = sp.GetServices<Abstractions.IKunConfigurationValidator<TOptions>>();
            return new KunOptionsValidator<TOptions>(section, validators);
        });

        return services.AddOptions<TOptions>()
            .BindConfiguration(section)
            .ValidateOnStart();
    }
}
