using System.Text.Json;

namespace Kun.EventBus;

/// <summary>Shared <see cref="JsonSerializerOptions"/> for consistent outbox serialization.</summary>
internal static class KunJsonHelpers
{
    /// <summary>Default options: camelCase, write-indented off, enum as string.</summary>
    public static readonly JsonSerializerOptions DefaultOptions = new(JsonSerializerDefaults.Web);
}
