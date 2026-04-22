using System.ComponentModel.DataAnnotations;

namespace Kun.Configuration.Attributes;

/// <summary>
/// Validates that the string value parses as an absolute URI via
/// <see cref="Uri.TryCreate(string?, UriKind, out Uri?)"/>. Accepts
/// <see langword="null"/> — pair with
/// <see cref="RequiredConfigAttribute"/> when the key is mandatory.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public sealed class UrlConfigAttribute : ValidationAttribute
{
    /// <inheritdoc />
    public override bool IsValid(object? value)
    {
        if (value is null)
        {
            return true;
        }
        return value is string s
            && !string.IsNullOrWhiteSpace(s)
            && Uri.TryCreate(s, UriKind.Absolute, out _);
    }

    /// <inheritdoc />
    public override string FormatErrorMessage(string name)
        => $"'{name}' must be a valid absolute URL.";
}
