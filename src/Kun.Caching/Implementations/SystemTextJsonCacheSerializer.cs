using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Kun.Caching.Abstractions;

namespace Kun.Caching.Implementations;

/// <summary>
/// Default <see cref="IKunCacheSerializer"/> backed by
/// <see cref="JsonSerializer"/>. Uses web-style options (camelCase,
/// case-insensitive reads) and respects the caller's
/// <see cref="JsonSerializerOptions"/> when supplied.
/// </summary>
/// <remarks>
/// Reflection-based; annotate any non-AOT-safe call site with the same
/// attributes or swap this impl for a source-generated one registered
/// via <c>services.AddSingleton&lt;IKunCacheSerializer, MyCtxSerializer&gt;()</c>.
/// </remarks>
public sealed class SystemTextJsonCacheSerializer : IKunCacheSerializer
{
    private readonly JsonSerializerOptions _options;

    /// <summary>Creates a serializer with the default web-style options.</summary>
    public SystemTextJsonCacheSerializer() : this(new JsonSerializerOptions(JsonSerializerDefaults.Web))
    {
    }

    /// <summary>Creates a serializer with custom <paramref name="options"/>.</summary>
    public SystemTextJsonCacheSerializer(JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    /// <inheritdoc />
    [RequiresUnreferencedCode("Reflection-based JSON serialization.")]
    [RequiresDynamicCode("Reflection-based JSON serialization.")]
    public byte[] Serialize<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, _options);

    /// <inheritdoc />
    [RequiresUnreferencedCode("Reflection-based JSON serialization.")]
    [RequiresDynamicCode("Reflection-based JSON serialization.")]
    public T? Deserialize<T>(byte[] data) => JsonSerializer.Deserialize<T>(data, _options);
}
