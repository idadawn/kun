using System.ComponentModel.DataAnnotations;

namespace Kun.Configuration.Attributes;

/// <summary>
/// <see cref="RequiredAttribute"/> tuned for configuration errors —
/// rejects <see langword="null"/> values and produces an error
/// message that names the missing key rather than the generic
/// "field is required" text.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public sealed class RequiredConfigAttribute : RequiredAttribute
{
    /// <summary>Creates the attribute with config-aware defaults.</summary>
    public RequiredConfigAttribute()
    {
        AllowEmptyStrings = false;
    }

    /// <inheritdoc />
    public override string FormatErrorMessage(string name)
        => $"'{name}' is required but missing in configuration.";
}
