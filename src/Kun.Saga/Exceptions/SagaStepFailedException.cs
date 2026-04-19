namespace Kun.Saga.Exceptions;

/// <summary>
/// Thrown by the saga step executor when a step's <c>ExecuteAsync</c> or <c>CompensateAsync</c>
/// terminates with an unhandled exception. The inner exception carries the original failure.
/// </summary>
public sealed class SagaStepFailedException : Exception
{
    /// <summary>Gets the name of the step that failed.</summary>
    public string StepName { get; }

    /// <summary>
    /// Initializes a new <see cref="SagaStepFailedException"/>.
    /// </summary>
    /// <param name="stepName">The name of the step that failed.</param>
    /// <param name="innerException">The exception raised by the step.</param>
    public SagaStepFailedException(string stepName, Exception innerException)
        : base($"Saga step '{stepName}' failed: {innerException.Message}", innerException)
    {
        ArgumentNullException.ThrowIfNull(stepName);
        StepName = stepName;
    }
}
