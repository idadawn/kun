using System.Text.Json.Serialization;

namespace Kun.Sandbox.Abstractions;

/// <summary>
/// Outcome of a completed <see cref="IProcesses.RunAsync(string[], ProcessRunOptions?, System.Threading.CancellationToken)"/>
/// call. Wire shape matches the E2B v2 <c>processes</c> response.
/// </summary>
public sealed class ProcessResult
{
    /// <summary>Backend-assigned id for this process invocation.</summary>
    [JsonPropertyName("processId")]
    public string ProcessId { get; init; } = string.Empty;

    /// <summary>Process exit code. <c>0</c> for clean exits; <c>-1</c> when the backend killed it before exit (e.g. timeout).</summary>
    [JsonPropertyName("exitCode")]
    public int ExitCode { get; init; }

    /// <summary>Captured stdout as UTF-8 text.</summary>
    [JsonPropertyName("stdout")]
    public string Stdout { get; init; } = string.Empty;

    /// <summary>Captured stderr as UTF-8 text.</summary>
    [JsonPropertyName("stderr")]
    public string Stderr { get; init; } = string.Empty;

    /// <summary>Wall-clock duration in milliseconds.</summary>
    [JsonPropertyName("durationMs")]
    public long DurationMs { get; init; }

    /// <summary><see langword="true"/> when stdout / stderr were capped at the backend's configured size and trimmed.</summary>
    [JsonPropertyName("truncated")]
    public bool Truncated { get; init; }
}
