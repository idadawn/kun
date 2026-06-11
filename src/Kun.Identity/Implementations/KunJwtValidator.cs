using System.Security.Claims;
using Kun.Identity.Abstractions;
using Kun.Identity.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Kun.Identity.Implementations;

/// <summary>
/// <see cref="IJwtValidator"/> backed by
/// <see cref="JsonWebTokenHandler"/>. Resolves signing keys from the
/// configured JWKS URI via
/// <see cref="ConfigurationManager{TConfiguration}"/> (with automatic
/// rotation) or from statically registered keys when supplied. Returns
/// <see langword="null"/> on any validation failure — the caller
/// decides whether to 401, ignore, or escalate.
/// </summary>
public sealed partial class KunJwtValidator : IJwtValidator
{
    private readonly KunJwtBearerOptions _options;
    private readonly ILogger<KunJwtValidator> _logger;
    private readonly JsonWebTokenHandler _handler = new();
    private readonly ConfigurationManager<OpenIdConnectConfiguration>? _configManager;

    /// <summary>Creates a validator bound to the supplied options.</summary>
    public KunJwtValidator(IOptions<KunJwtBearerOptions> options, ILogger<KunJwtValidator> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _options = options.Value;
        _logger = logger;

        if (string.IsNullOrWhiteSpace(_options.Issuer))
            throw new InvalidOperationException("KunJwtBearerOptions.Issuer must be configured.");
        if (string.IsNullOrWhiteSpace(_options.Audience))
            throw new InvalidOperationException("KunJwtBearerOptions.Audience must be configured.");

#pragma warning disable CS0618 // ValidateSignature is obsolete for callers, not for internal checks
        if (_options.ValidateSignature
            && (_options.IssuerSigningKeys?.Count ?? 0) == 0
            && string.IsNullOrWhiteSpace(_options.JwksUri))
        {
            throw new InvalidOperationException(
                "KunJwt: ValidateSignature is true but no IssuerSigningKeys and no JwksUri configured.");
        }

        if (!_options.ValidateSignature)
        {
            Log.SignatureValidationDisabled(_logger);
        }
#pragma warning restore CS0618

        if (!string.IsNullOrWhiteSpace(_options.JwksUri))
        {
            _configManager = new ConfigurationManager<OpenIdConnectConfiguration>(
                _options.JwksUri,
                new OpenIdConnectConfigurationRetriever(),
                new HttpDocumentRetriever());
        }
    }

    /// <inheritdoc />
    public async Task<ClaimsPrincipal?> ValidateAsync(string token, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        var parameters = new TokenValidationParameters
        {
            ValidIssuer = _options.Issuer,
            ValidateIssuer = true,
            ValidAudience = _options.Audience,
            ValidateAudience = true,
            RequireExpirationTime = _options.RequireExpirationTime,
            ValidateLifetime = true,
#pragma warning disable CS0618 // ValidateSignature is obsolete for callers, not for internal checks
            ValidateIssuerSigningKey = _options.ValidateSignature,
            RequireSignedTokens = _options.ValidateSignature,
#pragma warning restore CS0618
            ClockSkew = _options.ClockSkew,
        };

        if (_options.IssuerSigningKeys.Count > 0)
        {
            parameters.IssuerSigningKeys = [.. _options.IssuerSigningKeys];
        }
        else if (_configManager is not null)
        {
            try
            {
                var discovery = await _configManager.GetConfigurationAsync(cancellationToken).ConfigureAwait(false);
                parameters.IssuerSigningKeys = discovery.SigningKeys;
            }
            catch (Exception ex)
            {
                Log.JwksFetchFailed(_logger, _options.JwksUri ?? "<null>", ex);
                return null;
            }
        }

        var result = await _handler.ValidateTokenAsync(token, parameters).ConfigureAwait(false);
        if (!result.IsValid)
        {
            Log.TokenInvalid(_logger, result.Exception?.Message ?? "unknown");
            return null;
        }

        return new ClaimsPrincipal(result.ClaimsIdentity);
    }

    private static partial class Log
    {
        [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "Kun.Identity JWKS fetch failed for {JwksUri}.")]
        public static partial void JwksFetchFailed(ILogger logger, string jwksUri, Exception ex);

        [LoggerMessage(EventId = 2, Level = LogLevel.Debug, Message = "Kun.Identity token rejected: {Reason}")]
        public static partial void TokenInvalid(ILogger logger, string reason);

        [LoggerMessage(EventId = 3, Level = LogLevel.Critical, Message = "Kun.Identity: JWT signature validation is DISABLED. This must only be used in tests.")]
        public static partial void SignatureValidationDisabled(ILogger logger);
    }
}
