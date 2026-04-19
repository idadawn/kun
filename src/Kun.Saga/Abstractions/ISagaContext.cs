namespace Kun.Saga.Abstractions;

/// <summary>
/// Marker interface for saga context objects. Every saga context carries a unique
/// <see cref="SagaId"/> and an optional <see cref="TenantId"/> for multi-tenant scenarios.
/// Implementations should be serializable to JSON (used for durable state storage).
/// </summary>
public interface ISagaContext
{
    /// <summary>Gets the unique identifier for this saga instance.</summary>
    Guid SagaId { get; }

    /// <summary>
    /// Gets the tenant identifier; <see langword="null"/> for single-tenant deployments.
    /// </summary>
    string? TenantId { get; }
}
