using System.Security.Cryptography;
using Kun.Gateway.Endpoints;
using Microsoft.IdentityModel.Tokens;

namespace Kun.Gateway.Jwks;

/// <summary>
/// Caches the JWKS response keyed on (public-key path + file last-write time).
/// Re-reads the PEM, rebuilds the <see cref="JsonWebKey"/>, and materialises a new
/// <see cref="JwksResponse"/> only when the underlying file has been modified —
/// amortises the per-request cost of every JWKS fetch across downstream services
/// to a single <see cref="File.GetLastWriteTimeUtc(string)"/> stat after the first warm-up.
/// </summary>
/// <remarks>
/// <para>
/// Registered as a DI singleton by <c>AddKunGateway</c>. The cache is shared between
/// the public <c>MapKunJwks</c> route and the gated <c>MapKunInternalJwks</c> route
/// so both serve the same byte-identical document and both pay the key-rebuild
/// cost at most once per key-rotation event.
/// </para>
/// <para>
/// Thread-safe via a single lock around the cached tuple. The lock scope is tiny
/// (read the mtime, compare, maybe rebuild) and the rebuild path only fires when
/// an operator rotates the key — expected to be infrequent relative to request
/// volume, so lock contention is a non-issue in practice.
/// </para>
/// </remarks>
public sealed class JwksResponseBuilder
{
    private readonly object _lock = new();
    private CachedJwks? _cached;

    /// <summary>
    /// Returns the JWKS response for <paramref name="publicKeyPath"/>, rebuilding only
    /// when the file has been modified since the last successful build.
    /// </summary>
    /// <param name="publicKeyPath">Absolute path to the PEM-encoded RSA public key.</param>
    /// <returns>The cached or freshly-built <see cref="JwksResponse"/>.</returns>
    /// <exception cref="FileNotFoundException">The key file does not exist.</exception>
    public JwksResponse Build(string publicKeyPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publicKeyPath);

        var currentMtime = File.GetLastWriteTimeUtc(publicKeyPath);

        lock (_lock)
        {
            if (_cached is { } c
                && c.Path == publicKeyPath
                && c.LastWriteTimeUtc == currentMtime)
            {
                return c.Response;
            }

            var response = BuildCore(publicKeyPath);
            _cached = new CachedJwks(publicKeyPath, currentMtime, response);
            return response;
        }
    }

    private static JwksResponse BuildCore(string publicKeyPath)
    {
        var pem = File.ReadAllText(publicKeyPath);
        using var rsa = RSA.Create();
        rsa.ImportFromPem(pem);

        var rsaKey = new RsaSecurityKey(rsa);
        var jwk = JsonWebKeyConverter.ConvertFromRSASecurityKey(rsaKey);

        // Derive a stable kid from SHA-256 of the modulus bytes.
        var modulusBytes = rsa.ExportParameters(false).Modulus ?? [];
        var hash = SHA256.HashData(modulusBytes);
        jwk.KeyId = Convert.ToBase64String(hash)[..16];
        jwk.Use = "sig";
        jwk.Alg = "RS256";

        return new JwksResponse
        {
            Keys = [new JwkDto(jwk)],
        };
    }

    private sealed record CachedJwks(string Path, DateTime LastWriteTimeUtc, JwksResponse Response);
}
