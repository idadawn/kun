using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Kun.Security.S2SJwt;

/// <summary>
/// ES256 implementation of <see cref="IS2SJwtIssuer"/>.
/// Generates short-lived S2S JWTs per the NeuroAgent claim shape contract.
/// </summary>
public sealed partial class S2SJwtIssuer : IS2SJwtIssuer
{
    private readonly ECDsaSecurityKey _signingKey;
    private readonly SigningCredentials _signingCredentials;
    private readonly S2SJwtOptions _options;
    private readonly ILogger<S2SJwtIssuer> _logger;
    private readonly JwtSecurityTokenHandler _handler = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="S2SJwtIssuer"/> class.
    /// </summary>
    /// <param name="options">S2S JWT signing options.</param>
    /// <param name="logger">Logger for token issuance.</param>
    public S2SJwtIssuer(IOptions<S2SJwtOptions> options, ILogger<S2SJwtIssuer> logger)
    {
        _options = options.Value;
        _logger = logger;

        var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(_options.SigningKeyPem);
        _signingKey = new ECDsaSecurityKey(ecdsa) { KeyId = _options.KeyId };
        _signingCredentials = new SigningCredentials(_signingKey, SecurityAlgorithms.EcdsaSha256);
    }

    /// <inheritdoc />
    public Task<string> IssueAsync(
        string userId, string tenantId, string[] scopes, string[] audience,
        string sessionId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var expires = now.AddSeconds(_options.DefaultLifetimeSeconds);
        var jti = Guid.NewGuid().ToString("N");

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId),
            new(JwtRegisteredClaimNames.Jti, jti),
            new("tenant_id", tenantId),
            new("scope", string.Join(' ', scopes)),
            new("session_id", sessionId),
        };

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = null,
            Expires = expires,
            NotBefore = now,
            IssuedAt = now,
            Subject = new ClaimsIdentity(claims),
            SigningCredentials = _signingCredentials,
            AdditionalHeaderClaims = new Dictionary<string, object> { { "kid", _options.KeyId } },
        };

        // Set audience as array
        if (audience.Length == 1)
        {
            descriptor.Audience = audience[0];
        }
        else if (audience.Length > 1)
        {
            descriptor.AdditionalHeaderClaims = new Dictionary<string, object>(descriptor.AdditionalHeaderClaims)
            {
                { "aud", audience },
            };
        }

        var token = _handler.CreateEncodedJwt(descriptor);
        Log.TokenIssued(_logger, userId, _options.Issuer, jti);

        return Task.FromResult(token);
    }

    private static partial class Log
    {
        [LoggerMessage(LogLevel.Debug, "Issued S2S JWT for user {UserId}, issuer {Issuer}, jti {Jti}")]
        public static partial void TokenIssued(ILogger logger, string userId, string issuer, string jti);
    }
}
