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
    /// In Development, if the signing-key file does not exist, a 2048-bit RSA key pair is generated
    /// and persisted to <c>SigningKeyPath</c> and <c>PublicKeyPath</c> so local runs work out of the box.
    /// In non-Development environments a missing key file throws <see cref="InvalidOperationException"/>
    /// to prevent silent self-signing with ephemeral keys in production.
    /// </summary>
    /// <param name="options">Gateway configuration options.</param>
    /// <param name="env">Host environment used to differentiate Development from Production.</param>
    public S2sTokenIssuer(IOptions<KunGatewayOptions> options, IHostEnvironment env)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(env);
        _options = options.Value;

        var rsa = RSA.Create();
        if (File.Exists(_options.SigningKeyPath))
        {
            rsa.ImportFromPem(File.ReadAllText(_options.SigningKeyPath));
        }
        else if (env.IsDevelopment())
        {
            rsa.KeySize = 2048;
            EnsureDirectory(_options.SigningKeyPath);
            File.WriteAllText(_options.SigningKeyPath, rsa.ExportRSAPrivateKeyPem());
            EnsureDirectory(_options.PublicKeyPath);
            File.WriteAllText(_options.PublicKeyPath, rsa.ExportRSAPublicKeyPem());
        }
        else
        {
            throw new InvalidOperationException(
                $"S2sTokenIssuer: signing key not found at '{_options.SigningKeyPath}'. " +
                "Production deployments must pre-provision the RSA key pair. " +
                "For development, set ASPNETCORE_ENVIRONMENT=Development.");
        }

        _signingKey = new RsaSecurityKey(rsa);
        _credentials = new SigningCredentials(_signingKey, SecurityAlgorithms.RsaSha256);
    }

    private static void EnsureDirectory(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
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
