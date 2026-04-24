// RS0026 flags "multiple overloads with optional parameters" as a
// binary-compat risk. Here the two RunAsync overloads differ in the
// first parameter type (string[] vs string) — overload resolution is
// unambiguous on the call site, and these shipped together from day
// one so there is no binary-compat staircase. Suppressing at file
// level keeps the E2B-shaped dual-signature intact.
#pragma warning disable RS0026

namespace Kun.Sandbox.Abstractions;

/// <summary>
/// Process-execution sub-API of an <see cref="ISandboxHandle"/>. Maps
/// to the E2B v2 <c>POST /v2/sandboxes/{id}/processes</c> endpoint.
/// </summary>
public interface IProcesses
{
    /// <summary>
    /// Runs a command inside the sandbox synchronously and returns its
    /// aggregated <see cref="ProcessResult"/> when it exits (or the
    /// process is killed by timeout).
    /// </summary>
    /// <param name="command">
    /// The command to run. When supplied as a single string it is
    /// interpreted by the sandbox shell; when supplied as an argv array
    /// it is exec'd directly. The array form is preferred because it
    /// does not require shell quoting.
    /// </param>
    /// <param name="options">Per-call options (cwd / envs / timeout / stdin).</param>
    /// <param name="cancellationToken">Cancellation token for the run.</param>
    /// <returns>Exit code, stdout, stderr, duration, truncation flag.</returns>
    Task<ProcessResult> RunAsync(
        string[] command,
        ProcessRunOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Convenience overload accepting a single shell command string.
    /// Equivalent to invoking <see cref="RunAsync(string[], ProcessRunOptions?, CancellationToken)"/>
    /// with <c>["/bin/sh", "-c", command]</c>; backends are free to
    /// interpret the string natively for efficiency.
    /// </summary>
    Task<ProcessResult> RunAsync(
        string command,
        ProcessRunOptions? options = null,
        CancellationToken cancellationToken = default);
}
