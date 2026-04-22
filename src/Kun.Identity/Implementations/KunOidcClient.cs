using System.Net.Http.Headers;
using System.Net.Http.Json;
using Kun.Identity.Abstractions;
using Kun.Identity.Configuration;

namespace Kun.Identity.Implementations;

/// <summary>
/// <see cref="IOidcClient"/> that resolves an OIDC provider's
/// well-known metadata via HTTP GET and exchanges authorization codes
/// at the published token endpoint. The metadata is cached for the
/// life of the client — long-running hosts should re-resolve on
/// discovery failures rather than keep a stale document.
/// </summary>
public sealed class KunOidcClient : IOidcClient, IDisposable
{
    private readonly KunOidcClientOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly SemaphoreSlim _discoveryGate = new(1, 1);
    private OidcConfiguration? _cached;
    private int _disposed;

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _discoveryGate.Dispose();
        }
    }

    /// <summary>Creates a client bound to the supplied options + HttpClient factory.</summary>
    public KunOidcClient(KunOidcClientOptions options, IHttpClientFactory httpClientFactory)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Authority);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ClientId);
        _options = options;
        _httpClientFactory = httpClientFactory;
    }

    /// <inheritdoc />
    public string Name => _options.Name;

    /// <inheritdoc />
    public async Task<OidcConfiguration> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        if (_cached is not null)
        {
            return _cached;
        }

        await _discoveryGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_cached is not null)
            {
                return _cached;
            }

            var client = _httpClientFactory.CreateClient(KunIdentityBuilder.OidcHttpClientName(_options.Name));
            var url = $"{_options.Authority.TrimEnd('/')}/.well-known/openid-configuration";
            var document = await client.GetFromJsonAsync(
                url,
                OidcJsonContext.Default.OidcDiscoveryDocument,
                cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"OIDC discovery returned an empty document for {_options.Name}.");

            _cached = new OidcConfiguration(
                Issuer: document.Issuer,
                AuthorizationEndpoint: document.AuthorizationEndpoint,
                TokenEndpoint: document.TokenEndpoint,
                UserInfoEndpoint: document.UserInfoEndpoint,
                JwksUri: document.JwksUri);
            return _cached;
        }
        finally
        {
            _discoveryGate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<OidcTokenResponse> ExchangeCodeAsync(
        string code,
        string redirectUri,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(redirectUri);

        var discovery = await DiscoverAsync(cancellationToken).ConfigureAwait(false);
        var client = _httpClientFactory.CreateClient(KunIdentityBuilder.OidcHttpClientName(_options.Name));

        using var request = new HttpRequestMessage(HttpMethod.Post, discovery.TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = redirectUri,
                ["client_id"] = _options.ClientId,
            }),
        };
        if (!string.IsNullOrWhiteSpace(_options.ClientSecret))
        {
            var basic = Convert.ToBase64String(
                System.Text.Encoding.UTF8.GetBytes($"{_options.ClientId}:{_options.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
        }

        using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var token = await response.Content
            .ReadFromJsonAsync(OidcJsonContext.Default.OidcTokenDocument, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"OIDC token endpoint returned an empty response for {_options.Name}.");

        return new OidcTokenResponse(
            AccessToken: token.AccessToken,
            IdToken: token.IdToken,
            RefreshToken: token.RefreshToken,
            TokenType: token.TokenType ?? "Bearer",
            ExpiresIn: token.ExpiresIn);
    }

}
