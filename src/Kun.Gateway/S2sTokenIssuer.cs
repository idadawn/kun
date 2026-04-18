using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Kun.Gateway.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Kun.Gateway;

/// <summary>
/// Issues short-lived internal S2S JWTs signed with the gateway's RSA private key.
/// The issued token copies <c>sub</c>, <c>tenant_id</c>, and <c>roles</c> claims from
/// the authenticated external principal, sets <c>aud=kun-internal</c> and a 60-second TTL.
/// </summary>
public sealed class S2sTokenIssuer
{
    private readonly RsaSecurityKey _signingKey;
    private readonly SigningCredentials _credentials;
    private readonly KunGatewayOptions _options;

    /// <summary>
    /// Initializes the issuer by loading the RSA private key from <see cref="KunGatewayOptions.SigningKeyPath"/>.
    /// </summary>
    /// <param name="options">Gateway configuration options.</param>
    public S2sTokenIssuer(IOptions<KunGatewayOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;

        var pem = File.ReadAllText(_options.SigningKeyPath);
        var rsa = RSA.Create();
        rsa.ImportFromPem(pem);
        _signingKey = new RsaSecurityKey(rsa);
        _credentials = new SigningCredentials(_signingKey, SecurityAlgorithms.RsaSha256);
    }

    /// <summary>
    /// Issues an internal S2S JWT for the given external <paramref name="externalPrincipal"/>.
    /// Copies <c>sub</c>, <c>tenant_id</c>, and all <c>roles</c> claims into the new token.
    /// </summary>
    /// <param name="externalPrincipal">The authenticated principal from the external IdP JWT.</param>
    /// <returns>A signed RS256 JWT string.</returns>
    public string IssueFor(ClaimsPrincipal externalPrincipal)
    {
        ArgumentNullException.ThrowIfNull(externalPrincipal);

        var now = DateTime.UtcNow;
        var claims = new List<Claim>();

        // Copy sub claim
        var sub = externalPrincipal.FindFirst(JwtRegisteredClaimNames.Sub)
            ?? externalPrincipal.FindFirst(ClaimTypes.NameIdentifier);
        if (sub is not null)
            claims.Add(new Claim(JwtRegisteredClaimNames.Sub, sub.Value));

        // Copy tenant_id claim
        var tenantId = externalPrincipal.FindFirst("tenant_id");
        if (tenantId is not null)
            claims.Add(new Claim("tenant_id", tenantId.Value));

        // Copy all roles claims
        foreach (var role in externalPrincipal.FindAll(ClaimTypes.Role))
            claims.Add(new Claim("roles", role.Value));

        foreach (var role in externalPrincipal.FindAll("roles"))
            claims.Add(new Claim("roles", role.Value));

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.InternalIssuer,
            Audience = _options.InternalAudience,
            IssuedAt = now,
            NotBefore = now,
            Expires = now.Add(_options.InternalTokenLifetime),
            Subject = new ClaimsIdentity(claims),
            SigningCredentials = _credentials,
        };

        var handler = new JwtSecurityTokenHandler();
        return handler.CreateEncodedJwt(descriptor);
    }
}
