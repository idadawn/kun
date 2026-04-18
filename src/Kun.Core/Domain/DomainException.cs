namespace Kun.Core.Domain;

/// <summary>
/// Base exception for systemic domain-level failures that are not expressible via <c>Result&lt;T&gt;</c>.
/// <para>
/// Kun handler code should prefer <see cref="Kun.Core.Results.Result{T}"/> for business failures.
/// <c>DomainException</c> is reserved for exceptional states the framework itself surfaces
/// (concurrency conflicts, repository contract violations, infrastructure-level faults).
/// </para>
/// </summary>
public abstract class DomainException : Exception
{
    /// <summary>Stable machine-readable error code (e.g. <c>"order.concurrencyConflict"</c>).</summary>
    public string Code { get; }

    /// <summary>Initializes a new instance with a stable error <paramref name="code"/> and <paramref name="message"/>.</summary>
    protected DomainException(string code, string message) : base(message)
    {
        ArgumentNullException.ThrowIfNull(code);
        Code = code;
    }

    /// <summary>Initializes a new instance wrapping an <paramref name="innerException"/>.</summary>
    protected DomainException(string code, string message, Exception innerException)
        : base(message, innerException)
    {
        ArgumentNullException.ThrowIfNull(code);
        Code = code;
    }
}
