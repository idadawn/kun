using Microsoft.Extensions.Options;

namespace Kun.Configuration.Abstractions;

/// <summary>
/// Reflection-free options validator — the AOT-safe alternative to
/// DataAnnotations. Register one per options type via
/// <c>services.AddSingleton&lt;IKunConfigurationValidator&lt;T&gt;, MyValidator&gt;()</c>
/// and <c>AddKunOptions&lt;T&gt;</c> will merge the results with any
/// DataAnnotations-driven validation.
/// </summary>
/// <typeparam name="TOptions">The options type being validated.</typeparam>
public interface IKunConfigurationValidator<in TOptions> where TOptions : class
{
    /// <summary>
    /// Validates <paramref name="options"/>. Return
    /// <see cref="ValidateOptionsResult.Success"/> on success or
    /// <see cref="ValidateOptionsResult.Fail(System.Collections.Generic.IEnumerable{string})"/>
    /// with section-qualified messages on failure.
    /// </summary>
    /// <param name="name">The named-options name, or <see langword="null"/> for the default instance.</param>
    /// <param name="options">The populated options object.</param>
    ValidateOptionsResult Validate(string? name, TOptions options);
}
