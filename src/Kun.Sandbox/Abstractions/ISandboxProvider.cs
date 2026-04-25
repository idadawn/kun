namespace Kun.Sandbox.Abstractions;

/// <summary>
/// Pluggable sandbox backend. Creates an <see cref="ISandboxHandle"/>
/// that represents a short-lived isolated execution environment.
/// </summary>
/// <remarks>
/// The contract is deliberately aligned with the E2B v2 <c>POST /v2/sandboxes</c>
/// API so that consumers can swap between the in-memory fake, the
/// self-hosted NeuroAgent Supervisor, and the E2B Cloud backend (Mode D)
/// without touching call sites. See NeuroAgent Sandbox Spec v1.1 §4.3.
/// </remarks>
public interface ISandboxProvider
{
    /// <summary>
    /// Creates a new sandbox with the supplied options and returns a
    /// handle for process / file operations. The sandbox is <c>running</c>
    /// when the task completes.
    /// </summary>
    /// <param name="options">Sandbox creation options. Mirrors the E2B v2 create payload plus a NeuroAgent extension sub-object.</param>
    /// <param name="cancellationToken">Cancellation token for the create call.</param>
    /// <returns>A handle whose lifetime is tied to the sandbox.</returns>
    Task<ISandboxHandle> CreateAsync(SandboxCreateOptions options, CancellationToken cancellationToken = default);
}
