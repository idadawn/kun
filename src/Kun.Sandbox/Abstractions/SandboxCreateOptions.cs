using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kun.Sandbox.Abstractions;

/// <summary>
/// Sandbox create options use the portable E2B v2 fields plus explicitly named provider-owned extension data.
/// </summary>
public sealed class SandboxCreateOptions
{
    /// <summary>Template id (e.g. <c>kun-default</c>).</summary>
    [JsonPropertyName("templateId")]
    public string TemplateId { get; init; } = "kun-default";

    /// <summary>vCPU count requested for the sandbox.</summary>
    [JsonPropertyName("cpuCount")]
    public int CpuCount { get; init; } = 2;

    /// <summary>Memory limit in megabytes.</summary>
    [JsonPropertyName("memoryMB")]
    public int MemoryMB { get; init; } = 2048;

    /// <summary>Disk limit in megabytes (optional; when null, backend default applies).</summary>
    [JsonPropertyName("diskMB")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? DiskMB { get; init; }

    /// <summary>Hard timeout in milliseconds. The sandbox is torn down after this wall-clock interval.</summary>
    [JsonPropertyName("timeoutMs")]
    public int TimeoutMs { get; init; } = 300_000;

    /// <summary>Environment variables to inject into the sandbox. Treated as "low trust" — prefer <see cref="NeuroagentSandboxExtension.SecretScope"/> for credentials.</summary>
    [JsonPropertyName("envs")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string, string>? Envs { get; init; }

    /// <summary>
    /// Free-form application metadata, such as <c>workspaceId</c>, <c>threadId</c>,
    /// <c>tenantId</c>, <c>agentDefId</c> for audit correlation.
    /// </summary>
    [JsonPropertyName("metadata")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }

    /// <summary>
    /// Legacy explicit wire field. Retained for existing consumers; never emitted by default.
    /// </summary>
    [JsonPropertyName("neuroagent")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [Obsolete("Use ProviderExtensions with an explicitly selected provider-owned key.")]
    public NeuroagentSandboxExtension? NeuroagentExtension { get; init; }
    /// <summary>Provider-owned top-level JSON members. The caller explicitly chooses keys and values; core fields cannot be overridden.</summary>
    // 源生成的扩展数据需要普通 setter；init 会被当作构造参数而无法绑定。
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ProviderExtensions { get; set; }

}

/// <summary>
/// Compatibility shape for the legacy explicit supervisor extension. New integrations choose their own provider extension contract.
/// </summary>
[Obsolete("Use provider-owned JSON extension data on SandboxCreateOptions.ProviderExtensions.")]
public sealed class NeuroagentSandboxExtension
{
    /// <summary>Secret scope configuration — names of secret bundles to be made available via the metadata service.</summary>
    [JsonPropertyName("secretScope")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SecretScopeConfig? SecretScope { get; init; }

    /// <summary>Network egress policy label (e.g. <c>tenant-default</c>, <c>allow-all</c>, <c>deny-all</c>).</summary>
    [JsonPropertyName("networkPolicy")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? NetworkPolicy { get; init; }

    /// <summary>Browser session configuration.</summary>
    [JsonPropertyName("browser")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public BrowserSessionConfig? Browser { get; init; }

    /// <summary>Maximum number of commands permitted during the sandbox lifetime. Exceeding this aborts the sandbox.</summary>
    [JsonPropertyName("maxCommands")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MaxCommands { get; init; }
}

/// <summary>Secret-scope sub-object of <see cref="NeuroagentSandboxExtension"/>.</summary>
public sealed class SecretScopeConfig
{
    /// <summary>Scope names (e.g. <c>github_read</c>, <c>company_crm_read</c>).</summary>
    [JsonPropertyName("scopes")]
    public IReadOnlyList<string> Scopes { get; init; } = [];
}

/// <summary>Browser configuration sub-object of <see cref="NeuroagentSandboxExtension"/>.</summary>
public sealed class BrowserSessionConfig
{
    /// <summary>Whether the browser subsystem should be available in the sandbox.</summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; }

    /// <summary>Optional id of a prior browser session to resume.</summary>
    [JsonPropertyName("resumeSessionId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ResumeSessionId { get; init; }
}
