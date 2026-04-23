using System.Text.Json.Serialization;

namespace Kun.Sandbox.Abstractions;

/// <summary>
/// Per-call options for <see cref="IProcesses.RunAsync(string[], ProcessRunOptions?, System.Threading.CancellationToken)"/>.
/// Mirrors the E2B v2 <c>POST /v2/sandboxes/{id}/processes</c> request body.
/// </summary>
public sealed class ProcessRunOptions
{
    /// <summary>Working directory for the process. Defaults to the sandbox home when null.</summary>
    [JsonPropertyName("cwd")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Cwd { get; init; }

    /// <summary>Extra environment variables merged on top of the sandbox-wide envs.</summary>
    [JsonPropertyName("envs")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string, string>? Envs { get; init; }

    /// <summary>Per-command timeout in milliseconds. Null = backend default.</summary>
    [JsonPropertyName("timeoutMs")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? TimeoutMs { get; init; }

    /// <summary>Optional stdin payload streamed to the process.</summary>
    [JsonPropertyName("stdin")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Stdin { get; init; }

    /// <summary>
    /// When <see langword="true"/>, start the process detached and
    /// return its id immediately (maps to E2B <c>background</c>). The
    /// synchronous <see cref="IProcesses.RunAsync(string[], ProcessRunOptions?, System.Threading.CancellationToken)"/>
    /// method **does not** support this; streaming is an out-of-scope
    /// v1 follow-up.
    /// </summary>
    [JsonPropertyName("background")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Background { get; init; }
}
