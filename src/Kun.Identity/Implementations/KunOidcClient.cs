#pragma warning disable RS0026 // Preserve the legacy string signature beside the typed PKCE request.
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kun.Identity.Abstractions;
using Kun.Identity.Configuration;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Kun.Identity.Implementations;

/// <summary>OIDC protocol client with bounded metadata caching and signed, issuer/audience/nonce-bound ID Tokens.</summary>
public sealed class KunOidcClient : IOidcClient, IDisposable
{
    private readonly KunOidcClientOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TimeProvider _clock;
    private readonly SemaphoreSlim _discoveryGate = new(1, 1);
    private DiscoverySnapshot? _cached;
    private int _disposed;

    /// <summary>Creates a client using the system clock.</summary>
    public KunOidcClient(KunOidcClientOptions options, IHttpClientFactory httpClientFactory) : this(options, httpClientFactory, TimeProvider.System) { }

    /// <summary>Creates a client with an explicit clock for metadata freshness and token lifetimes.</summary>
    public KunOidcClient(KunOidcClientOptions options, IHttpClientFactory httpClientFactory, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ClientId);
        ArgumentOutOfRangeException.ThrowIfNegative(options.DiscoveryRefreshInterval.Ticks);
        _options = options; _httpClientFactory = httpClientFactory; _clock = clock;
        ValidateEndpoint(options.Authority);
    }

    /// <inheritdoc />
    public string Name => _options.Name;

    /// <inheritdoc />
    public async Task<OidcConfiguration> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        var snapshot = Volatile.Read(ref _cached);
        if (snapshot is not null && _clock.GetUtcNow() < snapshot.ExpiresAt) return snapshot.Configuration;
        await _discoveryGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            snapshot = Volatile.Read(ref _cached);
            if (snapshot is not null && _clock.GetUtcNow() < snapshot.ExpiresAt) return snapshot.Configuration;
            var client = Client();
            var document = await client.GetFromJsonAsync($"{_options.Authority.TrimEnd('/')}/.well-known/openid-configuration",
                OidcJsonContext.Default.OidcDiscoveryDocument, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("OIDC discovery returned an empty document.");
            if (!string.Equals(document.Issuer.TrimEnd('/'), _options.Authority.TrimEnd('/'), StringComparison.Ordinal))
                throw new SecurityTokenInvalidIssuerException("OIDC discovery issuer does not match the configured authority.");
            ValidateEndpoint(document.Issuer); ValidateEndpoint(document.AuthorizationEndpoint); ValidateEndpoint(document.TokenEndpoint); ValidateEndpoint(document.JwksUri);
            if (document.UserInfoEndpoint is not null) ValidateEndpoint(document.UserInfoEndpoint);
            var configuration = new OidcConfiguration(document.Issuer, document.AuthorizationEndpoint, document.TokenEndpoint, document.UserInfoEndpoint, document.JwksUri);
            Volatile.Write(ref _cached, new DiscoverySnapshot(configuration, _clock.GetUtcNow() + _options.DiscoveryRefreshInterval));
            return configuration;
        }
        finally { _discoveryGate.Release(); }
    }

    /// <inheritdoc />
    [Obsolete("Pass an OidcCodeExchange with PKCE and nonce.", DiagnosticId = "KUNOIDC001")]
    public Task<OidcTokenResponse> ExchangeCodeAsync(string code, string redirectUri, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Authorization code exchange requires the original PKCE verifier and nonce.");

    /// <inheritdoc />
    public async Task<OidcTokenResponse> ExchangeCodeAsync(OidcCodeExchange request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Code);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.RedirectUri);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ExpectedNonce);
        if (request.CodeVerifier is null || request.CodeVerifier.Length is < 43 or > 128 || request.CodeVerifier.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '.' and not '_' and not '~'))
            throw new ArgumentException("PKCE verifier must contain 43-128 unreserved ASCII characters.", nameof(request));
        var discovery = await DiscoverAsync(cancellationToken).ConfigureAwait(false);
        var tokens = await GrantAsync(discovery, new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = request.Code,
            ["redirect_uri"] = request.RedirectUri,
            ["code_verifier"] = request.CodeVerifier
        }, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(tokens.IdToken)) throw new SecurityTokenValidationException("Code exchange did not return an ID Token.");
        var principal = await ValidateIdentityAsync(tokens.IdToken, discovery, cancellationToken).ConfigureAwait(false);
        var nonce = principal.FindFirst("nonce")?.Value;
        if (nonce is null || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(nonce), Encoding.UTF8.GetBytes(request.ExpectedNonce)))
            throw new SecurityTokenValidationException("OIDC nonce does not match the initiating transaction.");
        return tokens with { ValidatedPrincipal = principal };
    }

    /// <inheritdoc />
    public async Task<OidcTokenResponse> RefreshAsync(string refreshToken, string expectedSubject, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(refreshToken); ArgumentException.ThrowIfNullOrWhiteSpace(expectedSubject);
        var discovery = await DiscoverAsync(cancellationToken).ConfigureAwait(false);
        var tokens = await GrantAsync(discovery, new Dictionary<string, string> { ["grant_type"] = "refresh_token", ["refresh_token"] = refreshToken }, cancellationToken).ConfigureAwait(false);
        if (tokens.IdToken is null) return tokens;
        var principal = await ValidateIdentityAsync(tokens.IdToken, discovery, cancellationToken).ConfigureAwait(false);
        if (principal.FindFirst("sub")?.Value != expectedSubject) throw new SecurityTokenValidationException("Refresh changed the session subject.");
        return tokens with { ValidatedPrincipal = principal };
    }

    /// <inheritdoc />
    public async Task<OidcTokenResponse> ClientCredentialsAsync(string scope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (string.IsNullOrEmpty(_options.ClientSecret)) throw new InvalidOperationException("Client credentials require a configured client secret.");
        return await GrantAsync(await DiscoverAsync(cancellationToken).ConfigureAwait(false), new Dictionary<string, string>
        { ["grant_type"] = "client_credentials", ["scope"] = scope }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<OidcLogoutToken> ValidateLogoutTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        var discovery = await DiscoverAsync(cancellationToken).ConfigureAwait(false);
        var principal = await ValidateSignedAsync(token, discovery, cancellationToken).ConfigureAwait(false);
        var subject = principal.FindFirst("sub")?.Value; var sid = principal.FindFirst("sid")?.Value;
        if (string.IsNullOrEmpty(subject) && string.IsNullOrEmpty(sid)) throw new SecurityTokenValidationException("Logout token has neither sub nor sid.");
        if (principal.HasClaim(claim => claim.Type == "nonce")) throw new SecurityTokenValidationException("Logout token must not contain a nonce.");
        var jti = principal.FindFirst("jti")?.Value;
        if (string.IsNullOrWhiteSpace(jti)) throw new SecurityTokenValidationException("Logout token is missing jti.");
        var eventJson = principal.FindFirst("events")?.Value;
        if (eventJson is null) throw new SecurityTokenValidationException("Logout token is missing its event.");
        using var events = JsonDocument.Parse(eventJson);
        if (events.RootElement.ValueKind != JsonValueKind.Object || !events.RootElement.TryGetProperty("http://schemas.openid.net/event/backchannel-logout", out var logout) || logout.ValueKind != JsonValueKind.Object)
            throw new SecurityTokenValidationException("Logout token has an invalid event.");
        return new(discovery.Issuer, subject, sid, jti, DateTimeOffset.FromUnixTimeSeconds(long.Parse(principal.FindFirst("exp")!.Value, CultureInfo.InvariantCulture)));
    }

    private async Task<ClaimsPrincipal> ValidateIdentityAsync(string token, OidcConfiguration configuration, CancellationToken ct)
    {
        var principal = await ValidateSignedAsync(token, configuration, ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(principal.FindFirst("sub")?.Value) || principal.HasClaim(claim => claim.Type == "events"))
            throw new SecurityTokenValidationException("Token is not an ID Token with a subject.");
        return principal;
    }

    private async Task<ClaimsPrincipal> ValidateSignedAsync(string token, OidcConfiguration configuration, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        var keys = new JsonWebKeySet(await Client().GetStringAsync(configuration.JwksUri, ct).ConfigureAwait(false));
        var now = _clock.GetUtcNow().UtcDateTime;
        var skew = TimeSpan.FromSeconds(30);
        var handler = new JsonWebTokenHandler { MapInboundClaims = false };
        var result = await handler.ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = configuration.Issuer,
            ValidateAudience = true,
            ValidAudience = _options.ClientId,
            RequireSignedTokens = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = keys.GetSigningKeys(),
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256, SecurityAlgorithms.EcdsaSha256],
            RequireExpirationTime = true,
            ValidateLifetime = true,
            ClockSkew = skew,
            LifetimeValidator = (notBefore, expires, _, _) => expires is not null && expires > now - skew && (notBefore is null || notBefore <= now + skew)
        }).ConfigureAwait(false);
        if (!result.IsValid) throw new SecurityTokenValidationException("OIDC token signature, issuer, audience or lifetime is invalid.");
        var principal = new ClaimsPrincipal(result.ClaimsIdentity);
        if (!long.TryParse(principal.FindFirst("iat")?.Value, CultureInfo.InvariantCulture, out var issuedAt) || issuedAt > _clock.GetUtcNow().Add(skew).ToUnixTimeSeconds())
            throw new SecurityTokenValidationException("OIDC token has an invalid issue time.");
        if (principal.FindAll("aud").Any(claim => claim.Value != _options.ClientId) || principal.FindFirst("azp") is { } party && party.Value != _options.ClientId)
            throw new SecurityTokenValidationException("OIDC token has an untrusted additional audience or authorized party.");
        return principal;
    }

    private async Task<OidcTokenResponse> GrantAsync(OidcConfiguration discovery, Dictionary<string, string> form, CancellationToken ct)
    {
        form["client_id"] = _options.ClientId;
        using var request = new HttpRequestMessage(HttpMethod.Post, discovery.TokenEndpoint) { Content = new FormUrlEncodedContent(form) };
        if (!string.IsNullOrEmpty(_options.ClientSecret))
        {
            var credentials = WebUtility.UrlEncode(_options.ClientId) + ":" + WebUtility.UrlEncode(_options.ClientSecret);
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(credentials)));
        }
        using var response = await Client().SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var token = await response.Content.ReadFromJsonAsync(OidcJsonContext.Default.OidcTokenDocument, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("OIDC token endpoint returned an empty response.");
        if (string.IsNullOrWhiteSpace(token.AccessToken) || !string.Equals(token.TokenType, "Bearer", StringComparison.OrdinalIgnoreCase) || token.ExpiresIn <= 0)
            throw new SecurityTokenValidationException("OIDC token response is incomplete or uses an unsupported token type.");
        return new(token.AccessToken, token.IdToken, token.RefreshToken, token.TokenType!, token.ExpiresIn);
    }

    private HttpClient Client() => _httpClientFactory.CreateClient(KunIdentityBuilder.OidcHttpClientName(_options.Name));
    private void ValidateEndpoint(string endpoint)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme is not "https" and not "http" || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0 || _options.RequireHttpsMetadata && uri.Scheme != "https")
            throw new InvalidOperationException("OIDC provider endpoints must be absolute HTTPS URLs without user-info or fragments.");
    }

    /// <inheritdoc />
    public void Dispose() { if (Interlocked.Exchange(ref _disposed, 1) == 0) _discoveryGate.Dispose(); }
    private sealed record DiscoverySnapshot(OidcConfiguration Configuration, DateTimeOffset ExpiresAt);
}
