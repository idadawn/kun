using System.Text.Json.Serialization;
using Kun.Identity.Abstractions.Session;

namespace Kun.Identity.Implementations.Session;

/// <summary>
/// Source-generated JSON contract for <see cref="SessionData"/> and
/// its collection members. The Redis-backed session store routes every
/// serialization call through this context so the AOT analyzer never
/// trips the reflection-based default path of <see cref="System.Text.Json.JsonSerializer"/>.
/// </summary>
/// <remarks>
/// Keep this context minimal. New types added to <see cref="SessionData"/>
/// (e.g. an opaque metadata bag) must register here explicitly —
/// silently relying on fallback serialization defeats the point.
/// </remarks>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(SessionData))]
[JsonSerializable(typeof(IReadOnlyList<string>))]
[JsonSerializable(typeof(List<string>))]
internal sealed partial class SessionJsonContext : JsonSerializerContext
{
}
