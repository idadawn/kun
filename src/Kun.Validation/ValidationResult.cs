namespace Kun.Validation;

/// <summary>
/// Result of running a validator against an instance. <see cref="IsValid"/> is true iff
/// <see cref="Errors"/> is empty.
/// </summary>
public readonly record struct ValidationResult
{
    /// <summary>Errors accumulated by the validator. Empty collection means valid.</summary>
    public IReadOnlyList<ValidationError> Errors { get; }

    /// <summary>True iff <see cref="Errors"/> is empty.</summary>
    public bool IsValid => Errors.Count == 0;

    private ValidationResult(IReadOnlyList<ValidationError> errors)
    {
        Errors = errors;
    }

    /// <summary>A successful validation with no errors.</summary>
    public static ValidationResult Success { get; } = new(Array.Empty<ValidationError>());

    /// <summary>Create a failing validation result from the given errors.</summary>
    public static ValidationResult Failure(IReadOnlyList<ValidationError> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        if (errors.Count == 0)
            throw new ArgumentException("Failure must carry at least one error.", nameof(errors));
        return new ValidationResult(errors);
    }
}
