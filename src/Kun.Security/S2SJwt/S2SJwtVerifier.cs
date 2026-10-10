using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Kun.Security.S2SJwt;

/// <summary>
/// Verifies S2S JWT tokens using ES256 and the configured signing keys.
/// Supports key rotation by accepting both current and previous signing keys.
/// </summary>
[Obsolete("Use Kun.Gateway.S2sTokenIssuer and Kun.Security service authentication.", DiagnosticId = "KUNS2S001")]
public sealed partial class S2SJwtVerifier : IS2SJwtVerifier
{
    private readonly TokenValidationParameters _validationParams;
    private readonly ILogger<S2SJwtVerifier> _logger;
    private readonly JwtSecurityTokenHandler _handler = new() { MapInboundClaims = false };

    /// <summary>
    /// Initializes a new instance of the <see cref="S2SJwtVerifier"/> class.
    /// </summary>
    /// <param name="options">S2S JWT signing options (used to derive token validation parameters).</param>
    /// <param name="logger">Logger for verification outcomes.</param>
    public S2SJwtVerifier(IOptions<S2SJwtOptions> options, ILogger<S2SJwtVerifier> logger)
    {
        _logger = logger;
        var opts = options.Value;

        var signingKeys = new List<SecurityKey>();

        var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(opts.SigningKeyPem);
        signingKeys.Add(new ECDsaSecurityKey(ecdsa) { KeyId = opts.KeyId });

        if (!string.IsNullOrWhiteSpace(opts.PreviousSigningKeyPem))
        {
            var prevEcdsa = ECDsa.Create();
            prevEcdsa.ImportFromPem(opts.PreviousSigningKeyPem);
            signingKeys.Add(new ECDsaSecurityKey(prevEcdsa) { KeyId = opts.PreviousKeyId ?? "previous" });
        }

        _validationParams = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuers = [opts.Issuer],
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidAlgorithms = [SecurityAlgorithms.EcdsaSha256],
            IssuerSigningKeys = signingKeys,
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    }

    /// <inheritdoc />
    public Task<ClaimsPrincipal?> VerifyAsync(string token, string expectedAudience, CancellationToken ct = default)
    {
        // Set the expected audience per call
        var tvp = _validationParams.Clone();
        tvp.ValidAudience = expectedAudience;

        try
        {
            var principal = _handler.ValidateToken(token, tvp, out _);
            Log.TokenVerified(_logger, principal.FindFirst("sub")?.Value ?? "unknown");
            return Task.FromResult<ClaimsPrincipal?>(principal);
        }
        catch (SecurityTokenException ex)
        {
            Log.TokenRejected(_logger, ex.Message);
            return Task.FromResult<ClaimsPrincipal?>(null);
        }
    }

    private static partial class Log
    {
        [LoggerMessage(LogLevel.Debug, "S2S JWT verified for user {UserId}")]
        public static partial void TokenVerified(ILogger logger, string userId);

        [LoggerMessage(LogLevel.Warning, "S2S JWT rejected: {Reason}")]
        public static partial void TokenRejected(ILogger logger, string reason);
    }
}
