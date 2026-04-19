namespace Kun.Core.Time;

/// <summary>
/// Abstraction over the system clock. Used by Kun framework and business code in place of
/// <see cref="DateTimeOffset.UtcNow"/> so tests can inject a deterministic clock.
/// </summary>
public interface ISystemClock
{
    /// <summary>Current UTC time.</summary>
    DateTimeOffset UtcNow { get; }
}
