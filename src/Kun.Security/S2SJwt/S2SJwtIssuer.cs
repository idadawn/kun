using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Kun.Security.S2SJwt;

/// <summary>
/// ES256 implementation of <see cref="IS2SJwtIssuer"/>. Emits the SAD v3.0 §12 claim
/// set (<c>sub</c>, <c>tenant_id</c>, <c>roles</c>, optional <c>membership_id</c>,
/// optional <c>trace_id</c>) and retains the pre-existing <c>scope</c> / <c>session_id</c>
/// claims for OAuth2-style callers.
/// </summary>
public sealed partial class S2SJwtIssuer : IS2SJwtIssuer
{
    private readonly ECDsaSecurityKey _signingKey;
    private readonly SigningCredentials _signingCredentials;
    private readonly S2SJwtOptions _options;
    private readonly ILogger<S2SJwtIssuer> _logger;
    private readonly JwtSecurityTokenHandler _handler = new();

    /// <summary>Initializes a new instance of the <see cref="S2SJwtIssuer"/> class.</summary>
    public S2SJwtIssuer(IOptions<S2SJwtOptions> options, ILogger<S2SJwtIssuer> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _options = options.Value;
        _logger = logger;

        var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(_options.SigningKeyPem);
        _signingKey = new ECDsaSecurityKey(ecdsa) { KeyId = _options.KeyId };
        _signingCredentials = new SigningCredentials(_signingKey, SecurityAlgorithms.EcdsaSha256);
    }

    /// <inheritdoc />
    public Task<string> IssueAsync(S2SJwtIssueRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.UserId))
        {
            throw new ArgumentException("UserId must be set.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.TenantId))
        {
            throw new ArgumentException("TenantId must be set.", nameof(request));
        }

        if (request.Audience is null || request.Audience.Count == 0)
        {
            throw new ArgumentException("Audience must contain at least one entry.", nameof(request));
        }

        var now = DateTime.UtcNow;
        var lifetime = request.Lifetime ?? TimeSpan.FromSeconds(_options.DefaultLifetimeSeconds);
        var expires = now.Add(lifetime);
        var jti = Guid.NewGuid().ToString("N");

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, request.UserId),
            new(JwtRegisteredClaimNames.Jti, jti),
            new("tenant_id", request.TenantId),
        };

        if (request.Roles is { Count: > 0 } roles)
        {
            foreach (var role in roles)
            {
                claims.Add(new Claim("roles", role));
            }
        }

        if (request.Scopes is { Count: > 0 } scopes)
        {
            claims.Add(new Claim("scope", string.Join(' ', scopes)));
        }

        if (!string.IsNullOrEmpty(request.SessionId))
        {
            claims.Add(new Claim("session_id", request.SessionId));
        }

        if (!string.IsNullOrEmpty(request.MembershipId))
        {
            claims.Add(new Claim("membership_id", request.MembershipId));
        }

        if (!string.IsNullOrEmpty(request.TraceId))
        {
            claims.Add(new Claim("trace_id", request.TraceId));
        }

        // For multi-audience tokens we fold aud into the claim set instead of the
        // descriptor so the payload carries an aud array (not an aud header claim).
        string? singleAudience = null;
        if (request.Audience.Count == 1)
        {
            singleAudience = request.Audience[0];
        }
        else
        {
            foreach (var aud in request.Audience)
            {
                claims.Add(new Claim(JwtRegisteredClaimNames.Aud, aud));
            }
        }

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = singleAudience,
            Expires = expires,
            NotBefore = now,
            IssuedAt = now,
            Subject = new ClaimsIdentity(claims),
            SigningCredentials = _signingCredentials,
            AdditionalHeaderClaims = new Dictionary<string, object> { { "kid", _options.KeyId } },
        };

        var token = _handler.CreateEncodedJwt(descriptor);
        Log.TokenIssued(_logger, request.UserId, _options.Issuer, jti);

        return Task.FromResult(token);
    }

    /// <inheritdoc />
    public Task<string> IssueAsync(
        string userId,
        string tenantId,
        string[] scopes,
        string[] audience,
        string sessionId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(scopes);
        ArgumentNullException.ThrowIfNull(audience);

        var request = new S2SJwtIssueRequest(
            UserId: userId,
            TenantId: tenantId,
            Audience: audience,
            Scopes: scopes,
            SessionId: sessionId);

        return IssueAsync(request, ct);
    }

    private static partial class Log
    {
        [LoggerMessage(LogLevel.Debug, "Issued S2S JWT for user {UserId}, issuer {Issuer}, jti {Jti}")]
        public static partial void TokenIssued(ILogger logger, string userId, string issuer, string jti);
    }
}
