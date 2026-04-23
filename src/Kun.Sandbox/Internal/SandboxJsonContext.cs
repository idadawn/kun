using System.Text.Json.Serialization;
using Kun.Sandbox.Abstractions;

namespace Kun.Sandbox.Internal;

/// <summary>
/// Source-generated <see cref="JsonSerializerContext"/> for all types
/// that <see cref="Kun.Sandbox"/> serialises on the HTTP boundary.
/// Registered explicitly so the module stays AOT-reflection-free.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    WriteIndented = false)]
[JsonSerializable(typeof(SandboxCreateOptions))]
[JsonSerializable(typeof(NeuroagentSandboxExtension))]
[JsonSerializable(typeof(SecretScopeConfig))]
[JsonSerializable(typeof(BrowserSessionConfig))]
[JsonSerializable(typeof(ProcessRunOptions))]
[JsonSerializable(typeof(ProcessResult))]
[JsonSerializable(typeof(FileEntry))]
[JsonSerializable(typeof(IReadOnlyList<FileEntry>))]
[JsonSerializable(typeof(SandboxCreateResponse))]
[JsonSerializable(typeof(FileListResponse))]
[JsonSerializable(typeof(ProcessRunRequestBody))]
internal sealed partial class SandboxJsonContext : JsonSerializerContext
{
}

/// <summary>Envelope returned by the Supervisor for <c>POST /v2/sandboxes</c>.</summary>
internal sealed class SandboxCreateResponse
{
    [JsonPropertyName("sandboxId")]
    public string SandboxId { get; init; } = string.Empty;

    [JsonPropertyName("templateId")]
    public string TemplateId { get; init; } = string.Empty;

    [JsonPropertyName("state")]
    public string State { get; init; } = "running";

    [JsonPropertyName("startedAt")]
    public DateTimeOffset StartedAt { get; init; }

    [JsonPropertyName("endAt")]
    public DateTimeOffset EndAt { get; init; }
}

/// <summary>Envelope returned by the Supervisor for <c>GET /v2/sandboxes/{id}/files/list</c>.</summary>
internal sealed class FileListResponse
{
    [JsonPropertyName("entries")]
    public IReadOnlyList<FileEntry> Entries { get; init; } = [];

    [JsonPropertyName("truncated")]
    public bool Truncated { get; init; }
}

/// <summary>
/// Wire body for <c>POST /v2/sandboxes/{id}/processes</c> — combines
/// the caller's <see cref="ProcessRunOptions"/> with the resolved
/// command argv. Kept internal so consumers never accidentally build it
/// directly.
/// </summary>
internal sealed class ProcessRunRequestBody
{
    [JsonPropertyName("cmd")]
    public IReadOnlyList<string> Cmd { get; init; } = [];

    [JsonPropertyName("cwd")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Cwd { get; init; }

    [JsonPropertyName("envs")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string, string>? Envs { get; init; }

    [JsonPropertyName("timeoutMs")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? TimeoutMs { get; init; }

    [JsonPropertyName("stdin")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Stdin { get; init; }

    [JsonPropertyName("background")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Background { get; init; }
}
