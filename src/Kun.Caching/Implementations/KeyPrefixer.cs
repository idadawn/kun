using Kun.Caching.Configuration;

namespace Kun.Caching.Implementations;

/// <summary>Applies the configured <c>KunCachingOptions.KeyPrefix</c> to raw keys.</summary>
internal static class KeyPrefixer
{
    public static string Apply(KunCachingOptions options, string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return string.IsNullOrEmpty(options.KeyPrefix) ? key : options.KeyPrefix + key;
    }
}
