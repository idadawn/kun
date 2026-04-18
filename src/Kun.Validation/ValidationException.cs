namespace Kun.Validation;

/// <summary>
/// Thrown by <see cref="Behaviors.ValidationPipelineBehavior{TRequest,TResponse}"/>
/// when one or more validators report errors. Carries the full error list so
/// upstream handlers (e.g. ASP.NET Core exception handlers) can translate it.
/// </summary>
public sealed class ValidationException : Exception
{
    /// <summary>Errors reported by the validators.</summary>
    public IReadOnlyList<ValidationError> Errors { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="ValidationException"/> with the given errors.
    /// </summary>
    /// <param name="errors">The validation errors. Must not be null.</param>
    public ValidationException(IReadOnlyList<ValidationError> errors)
        : base($"Validation failed with {errors?.Count ?? 0} error(s).")
    {
        ArgumentNullException.ThrowIfNull(errors);
        Errors = errors;
    }
}
