using System.Diagnostics.CodeAnalysis;

namespace Kun.Caching.Abstractions;

/// <summary>
/// Pluggable serializer used by the Redis-backed cache to convert
/// cache entries to and from byte arrays. The default registration is
/// <c>Kun.Caching.Implementations.SystemTextJsonCacheSerializer</c>,
/// which is reflection-based; AOT-strict consumers provide a
/// source-generated implementation.
/// </summary>
public interface IKunCacheSerializer
{
    /// <summary>Serializes <paramref name="value"/> to a newly allocated byte array.</summary>
    [RequiresUnreferencedCode("Reflection-based default. Provide a source-generated IKunCacheSerializer for AOT safety.")]
    [RequiresDynamicCode("Reflection-based default. Provide a source-generated IKunCacheSerializer for AOT safety.")]
    byte[] Serialize<T>(T value);

    /// <summary>Deserializes <paramref name="data"/> to a <typeparamref name="T"/> instance.</summary>
    [RequiresUnreferencedCode("Reflection-based default. Provide a source-generated IKunCacheSerializer for AOT safety.")]
    [RequiresDynamicCode("Reflection-based default. Provide a source-generated IKunCacheSerializer for AOT safety.")]
    T? Deserialize<T>(byte[] data);
}
