using Microsoft.IdentityModel.Tokens;

namespace Kun.Security.Jwks;

/// <summary>Provides the JSON Web Key Set used for S2S token signature verification.</summary>
public interface IKunJwksProvider
{
    /// <summary>Returns the current <see cref="JsonWebKeySet"/>.</summary>
    /// <param name="ct">Cancellation token.</param>
    Task<JsonWebKeySet> GetAsync(CancellationToken ct = default);
}
