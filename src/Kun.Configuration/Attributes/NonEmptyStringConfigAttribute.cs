using System.ComponentModel.DataAnnotations;

namespace Kun.Configuration.Attributes;

/// <summary>
/// Validates that the string value is non-null and non-whitespace.
/// Combine with <see cref="RequiredConfigAttribute"/> when the key
/// must also be present in the configuration tree — by itself this
/// attribute accepts <see langword="null"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public sealed class NonEmptyStringConfigAttribute : ValidationAttribute
{
    /// <inheritdoc />
    public override bool IsValid(object? value)
    {
        if (value is null)
        {
            return true;
        }
        return value is string s && !string.IsNullOrWhiteSpace(s);
    }

    /// <inheritdoc />
    public override string FormatErrorMessage(string name)
        => $"'{name}' must be a non-empty string.";
}
