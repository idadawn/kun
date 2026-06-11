using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Kun.Security.Authentication;

/// <summary>
/// <see cref="IConfigurationRetriever{T}"/> that loads a raw JWKS document
/// (RFC 7517, <c>{ "keys": [ ... ] }</c>) and exposes its signing keys as an
/// <see cref="OpenIdConnectConfiguration"/>.
/// </summary>
/// <remarks>
/// The built-in <see cref="OpenIdConnectConfigurationRetriever"/> expects an OIDC
/// discovery document (<c>.well-known/openid-configuration</c>), not a bare JWKS.
/// Pointing it at a JWKS endpoint therefore surfaces zero signing keys and every
/// token fails with <c>IDX10500</c>. This retriever parses the JWKS directly while
/// still being driven by a <see cref="ConfigurationManager{T}"/>, so keys are
/// cached and refreshed on an unknown <c>kid</c> rather than fetched per request.
/// </remarks>
internal sealed class JwksConfigurationRetriever : IConfigurationRetriever<OpenIdConnectConfiguration>
{
    /// <inheritdoc />
    public async Task<OpenIdConnectConfiguration> GetConfigurationAsync(
        string address,
        IDocumentRetriever retriever,
        CancellationToken cancel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(address);
        ArgumentNullException.ThrowIfNull(retriever);

        var json = await retriever.GetDocumentAsync(address, cancel).ConfigureAwait(false);
        var keySet = new JsonWebKeySet(json);

        var config = new OpenIdConnectConfiguration { JsonWebKeySet = keySet };
        foreach (var key in keySet.GetSigningKeys())
        {
            config.SigningKeys.Add(key);
        }

        return config;
    }
}
