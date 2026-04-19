namespace Kun.Core.Time;

/// <summary>
/// Default <see cref="ISystemClock"/> implementation backed by <see cref="TimeProvider.System"/>.
/// </summary>
public sealed class SystemClock : ISystemClock
{
    /// <summary>Singleton instance — <see cref="SystemClock"/> has no state.</summary>
    public static SystemClock Instance { get; } = new();

    private SystemClock() { }

    /// <inheritdoc />
    public DateTimeOffset UtcNow => TimeProvider.System.GetUtcNow();
}
