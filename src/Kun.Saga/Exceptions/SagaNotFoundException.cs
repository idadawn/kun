namespace Kun.Saga.Exceptions;

/// <summary>
/// Thrown when a saga instance cannot be found in the persistent store by its
/// <see cref="SagaId"/>, typically indicating the instance was never started or has been
/// purged.
/// </summary>
public sealed class SagaNotFoundException : Exception
{
    /// <summary>Gets the saga instance ID that was not found.</summary>
    public Guid SagaId { get; }

    /// <summary>
    /// Initializes a new <see cref="SagaNotFoundException"/>.
    /// </summary>
    /// <param name="sagaId">The saga instance ID that was not found.</param>
    public SagaNotFoundException(Guid sagaId)
        : base($"Saga instance '{sagaId}' was not found.")
    {
        SagaId = sagaId;
    }
}
