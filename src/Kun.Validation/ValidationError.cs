namespace Kun.Validation;

/// <summary>
/// A single validation failure on a specific property or at the object level.
/// </summary>
/// <param name="PropertyName">The name of the property that failed validation, or empty string for object-level errors.</param>
/// <param name="Code">A stable, machine-readable error code.</param>
/// <param name="Message">A human-readable description of the failure.</param>
public sealed record ValidationError(string PropertyName, string Code, string Message);
