using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using Kun.Configuration.Abstractions;
using Microsoft.Extensions.Options;

namespace Kun.Configuration.Internal;

/// <summary>
/// Internal <see cref="IValidateOptions{TOptions}"/> that runs
/// DataAnnotations validation (reflection-based) plus every
/// registered <see cref="IKunConfigurationValidator{TOptions}"/>
/// and prefixes every failure message with the configured section
/// path.
/// </summary>
internal sealed class KunOptionsValidator<TOptions> : IValidateOptions<TOptions>
    where TOptions : class
{
    private readonly string _sectionPath;
    private readonly IEnumerable<IKunConfigurationValidator<TOptions>> _validators;

    public KunOptionsValidator(
        string sectionPath,
        IEnumerable<IKunConfigurationValidator<TOptions>> validators)
    {
        _sectionPath = sectionPath;
        _validators = validators;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "DataAnnotations validator walks properties by reflection. Registration requires consumer to annotate TOptions with DynamicallyAccessedMemberTypes.All so the trimmer preserves them; see AddKunOptions.")]
    [UnconditionalSuppressMessage("AOT", "IL3050",
        Justification = "Same as IL2026 — consumer opts into reflection via AddKunOptions which is annotated with RequiresDynamicCode.")]
    public ValidateOptionsResult Validate(string? name, TOptions options)
    {
        var context = new ValidationContext(options);
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(options, context, results, validateAllProperties: true);

        var failures = new List<string>(results.Count);
        foreach (var result in results)
        {
            if (result.MemberNames.Any())
            {
                foreach (var member in result.MemberNames)
                {
                    failures.Add($"{_sectionPath}:{member} — {result.ErrorMessage}");
                }
            }
            else
            {
                failures.Add($"{_sectionPath} — {result.ErrorMessage}");
            }
        }

        foreach (var validator in _validators)
        {
            var custom = validator.Validate(name, options);
            if (custom.Failed && custom.Failures is not null)
            {
                foreach (var message in custom.Failures)
                {
                    failures.Add(message.StartsWith(_sectionPath, StringComparison.Ordinal)
                        ? message
                        : $"{_sectionPath} — {message}");
                }
            }
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
