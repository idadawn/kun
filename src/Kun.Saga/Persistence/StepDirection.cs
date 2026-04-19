namespace Kun.Saga.Persistence;

/// <summary>Indicates whether a queued saga step entry is a forward execution or a compensation.</summary>
public enum StepDirection
{
    /// <summary>The step should call <c>ISagaStep.ExecuteAsync</c>.</summary>
    Execute = 0,

    /// <summary>The step should call <c>ISagaStep.CompensateAsync</c>.</summary>
    Compensate = 1,
}
