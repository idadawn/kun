namespace Kun.Validation;

/// <summary>
/// Validates instances of <typeparamref name="T"/>. Implementations are registered per
/// request type via <c>KunMediatorBuilder.AddValidator&lt;T, TValidator&gt;()</c> (see
/// <see cref="DependencyInjection.KunValidationServiceCollectionExtensions"/>).
/// </summary>
/// <typeparam name="T">The type to validate.</typeparam>
public interface IValidator<in T>
{
    /// <summary>Validate the given instance.</summary>
    /// <param name="instance">The instance to validate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="ValidationResult"/> indicating success or failure.</returns>
    ValueTask<ValidationResult> ValidateAsync(T instance, CancellationToken cancellationToken = default);
}
