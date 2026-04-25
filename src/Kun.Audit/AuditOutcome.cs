namespace Kun.Audit;

/// <summary>Outcome of a command execution captured in an <see cref="AuditEntry"/>.</summary>
public enum AuditOutcome
{
    /// <summary>The handler returned normally (non-exception path).</summary>
    Success = 0,

    /// <summary>The handler threw an exception.</summary>
    Exception = 1,
}
