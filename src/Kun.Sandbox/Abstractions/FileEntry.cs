using System.Text.Json.Serialization;

namespace Kun.Sandbox.Abstractions;

/// <summary>
/// Single entry returned by <see cref="IFiles.ListAsync"/>. Wire shape
/// matches the E2B v2 <c>files/list</c> response.
/// </summary>
public sealed class FileEntry
{
    /// <summary>Absolute path inside the sandbox.</summary>
    [JsonPropertyName("path")]
    public string Path { get; init; } = string.Empty;

    /// <summary>Either <c>file</c> or <c>directory</c>.</summary>
    [JsonPropertyName("type")]
    public string Type { get; init; } = "file";

    /// <summary>Size in bytes. Zero for directories.</summary>
    [JsonPropertyName("sizeBytes")]
    public long SizeBytes { get; init; }

    /// <summary>Last-modified timestamp.</summary>
    [JsonPropertyName("modifiedAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? ModifiedAt { get; init; }
}
