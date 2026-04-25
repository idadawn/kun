namespace Kun.Sandbox.Abstractions;

/// <summary>
/// Handle to a running sandbox. Exposes process and file sub-APIs that
/// map one-to-one to E2B v2's <c>processes</c> and <c>files</c>
/// endpoints, plus a <see cref="KillAsync"/> operation that maps to
/// <c>DELETE /v2/sandboxes/{id}</c>.
/// </summary>
/// <remarks>
/// The handle is logically <see cref="IAsyncDisposable"/>-shaped — but
/// we intentionally do **not** implement the interface because disposal
/// semantics are not equivalent across backends (e.g. in-memory does
/// nothing, Supervisor does a best-effort DELETE, E2B Cloud may bill
/// partial seconds). Callers explicitly call <see cref="KillAsync"/>.
/// </remarks>
public interface ISandboxHandle
{
    /// <summary>Stable sandbox identifier returned by the backend.</summary>
    string SandboxId { get; }

    /// <summary>Template id the sandbox was created from.</summary>
    string TemplateId { get; }

    /// <summary>Current lifecycle state: <c>creating</c>, <c>running</c>, <c>killed</c>, <c>failed</c>, or <c>timedOut</c>.</summary>
    SandboxState State { get; }

    /// <summary>UTC timestamp the sandbox transitioned to <c>running</c>.</summary>
    DateTimeOffset StartedAt { get; }

    /// <summary>UTC timestamp the sandbox is scheduled to be torn down (start + <c>timeoutMs</c>).</summary>
    DateTimeOffset EndAt { get; }

    /// <summary>Process sub-API: run / stream / kill commands inside the sandbox.</summary>
    IProcesses Processes { get; }

    /// <summary>File sub-API: read / write / list / delete under the sandbox workspace.</summary>
    IFiles Files { get; }

    /// <summary>Destroys the sandbox. Safe to call multiple times; subsequent calls no-op when state is already <c>killed</c>.</summary>
    Task KillAsync(CancellationToken cancellationToken = default);
}

/// <summary>Lifecycle states exposed by <see cref="ISandboxHandle.State"/>. Matches the E2B v2 shape.</summary>
public enum SandboxState
{
    /// <summary>Default value; should not appear on a returned handle.</summary>
    Unknown = 0,

    /// <summary>Backend has accepted the create request; container is not yet ready.</summary>
    Creating,

    /// <summary>Container is ready; processes can run.</summary>
    Running,

    /// <summary>Sandbox was torn down via <see cref="ISandboxHandle.KillAsync"/> or because its timeout elapsed.</summary>
    Killed,

    /// <summary>Backend reported a failure during the sandbox lifetime.</summary>
    Failed,

    /// <summary>Sandbox was terminated because it exceeded its <c>timeoutMs</c>.</summary>
    TimedOut,
}
