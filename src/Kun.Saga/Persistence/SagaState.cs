namespace Kun.Saga.Persistence;

/// <summary>
/// Lifecycle state of a <see cref="SagaInstance"/>.
/// </summary>
public enum SagaState
{
    /// <summary>The saga is executing its forward steps.</summary>
    Running = 0,

    /// <summary>A step failed and the saga is executing compensation steps in reverse order.</summary>
    Compensating = 1,

    /// <summary>All forward steps completed successfully.</summary>
    Completed = 2,

    /// <summary>All compensation steps completed after a forward failure.</summary>
    Compensated = 3,

    /// <summary>
    /// The saga failed and compensation itself encountered an unrecoverable error.
    /// Manual intervention is required.
    /// </summary>
    Failed = 4,
}
