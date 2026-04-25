using System.Security.Cryptography;
using System.Text;
using Kun.Identity.Abstractions;
using Kun.Identity.AspNetCore.Internal;
using Kun.Identity.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Kun.Identity.AspNetCore;

/// <summary>
/// Builds the <c>/authorize</c> redirect URL for an OIDC provider
/// registered via <c>AddKunIdentity(id =&gt; id.AddOidcClient(...))</c>.
/// Generates the one-shot PKCE <c>code_verifier</c> /
/// <c>code_challenge</c> pair, a <c>state</c>, and a <c>nonce</c>,
/// stashes the verifier + state + nonce + returnUrl in a short-lived
/// cookie so the callback handler can replay them, and returns the
/// URL the caller should redirect the user to.
/// </summary>
public sealed class ChallengeHelper
{
    /// <summary>Name of the ephemeral challenge cookie.</summary>
    public const string ChallengeCookieName = "kun.challenge";

    /// <summary>Lifetime of the challenge cookie. Intentionally short — the IdP round-trip is seconds.</summary>
    public static readonly TimeSpan ChallengeCookieTtl = TimeSpan.FromMinutes(5);

    private readonly IEnumerable<IOidcClient> _oidcClients;
    private readonly IOptionsMonitor<KunOidcClientOptions> _oidcOptions;
    private readonly TimeProvider _timeProvider;

    /// <summary>Creates the helper.</summary>
    public ChallengeHelper(
        IEnumerable<IOidcClient> oidcClients,
        IOptionsMonitor<KunOidcClientOptions> oidcOptions,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(oidcClients);
        ArgumentNullException.ThrowIfNull(oidcOptions);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _oidcClients = oidcClients;
        _oidcOptions = oidcOptions;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Builds the login redirect URL. Side effect: writes the
    /// <see cref="ChallengeCookieName"/> cookie onto
    /// <paramref name="context"/> — the callback handler must read it
    /// to recover the PKCE verifier + state to validate.
    /// </summary>
    /// <param name="context">Current request context.</param>
    /// <param name="oidcClientName">Registered OIDC client name (e.g. <c>"authentik"</c>).</param>
    /// <param name="redirectUri">
    /// Absolute redirect URL the IdP will send the user back to after
    /// consent. Must match one of the URLs configured on the OIDC
    /// application.
    /// </param>
    /// <param name="returnUrl">
    /// Local URL to redirect to after sign-in completes; round-tripped
    /// through the challenge cookie. Validation against open-redirect
    /// is the caller's responsibility.
    /// </param>
    /// <param name="scope">OIDC scope parameter (default <c>openid profile email</c>).</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<string> BuildLoginUrlAsync(
        HttpContext context,
        string oidcClientName,
        string redirectUri,
        string returnUrl = "/",
        string scope = "openid profile email",
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(oidcClientName);
        ArgumentException.ThrowIfNullOrWhiteSpace(redirectUri);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);

        var oidc = FindClient(oidcClientName);
        var discovery = await oidc.DiscoverAsync(cancellationToken).ConfigureAwait(false);

        var codeVerifier = GenerateUrlSafeRandom(64);
        var codeChallenge = ComputeS256Challenge(codeVerifier);
        var state = GenerateUrlSafeRandom(24);
        var nonce = GenerateUrlSafeRandom(24);

        WriteChallengeCookie(context, new ChallengePayload(
            Client: oidcClientName,
            CodeVerifier: codeVerifier,
            State: state,
            Nonce: nonce,
            ReturnUrl: returnUrl));

        var clientOptions = _oidcOptions.Get(oidcClientName);
        if (string.IsNullOrWhiteSpace(clientOptions.ClientId))
        {
            throw new InvalidOperationException(
                $"OIDC client '{oidcClientName}' is not registered or its ClientId is empty. " +
                $"Call AddKunIdentity(id => id.AddOidcClient(\"{oidcClientName}\", o => o.ClientId = ...)).");
        }
        var clientId = clientOptions.ClientId;

        var query = new StringBuilder(discovery.AuthorizationEndpoint);
        query.Append(discovery.AuthorizationEndpoint.Contains('?', StringComparison.Ordinal) ? '&' : '?');
        query.Append("response_type=code");
        AppendQuery(query, "client_id", clientId);
        AppendQuery(query, "redirect_uri", redirectUri);
        AppendQuery(query, "scope", scope);
        AppendQuery(query, "state", state);
        AppendQuery(query, "nonce", nonce);
        AppendQuery(query, "code_challenge", codeChallenge);
        AppendQuery(query, "code_challenge_method", "S256");

        return query.ToString();
    }

    /// <summary>
    /// Reads and clears the challenge cookie written by
    /// <see cref="BuildLoginUrlAsync"/>. Returns <see langword="null"/>
    /// when the cookie is missing or malformed — callers treat that as
    /// a failed / replayed callback and respond with a 400.
    /// </summary>
    /// <remarks>
    /// The cookie is deleted as part of the read so even a replayed
    /// callback with the same cookie cannot succeed twice.
    /// </remarks>
    public static ChallengePayload? ConsumeChallengeCookie(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var raw = context.Request.Cookies[ChallengeCookieName];
        if (string.IsNullOrEmpty(raw))
        {
            return null;
        }

        context.Response.Cookies.Delete(ChallengeCookieName, new CookieOptions
        {
            Path = "/",
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.None,
        });

        return ChallengePayload.TryParse(raw, out var payload) ? payload : null;
    }

    private IOidcClient FindClient(string name)
    {
        foreach (var c in _oidcClients)
        {
            if (string.Equals(c.Name, name, StringComparison.Ordinal))
            {
                return c;
            }
        }
        throw new InvalidOperationException($"No IOidcClient registered under the name '{name}'. Call AddKunIdentity(id => id.AddOidcClient(\"{name}\", ...)).");
    }

    private static string GenerateUrlSafeRandom(int byteCount)
    {
        Span<byte> buffer = stackalloc byte[byteCount];
        RandomNumberGenerator.Fill(buffer);
        return Convert.ToBase64String(buffer)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    private static string ComputeS256Challenge(string verifier)
    {
        var bytes = Encoding.ASCII.GetBytes(verifier);
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(bytes, hash);
        return Convert.ToBase64String(hash)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    private void WriteChallengeCookie(HttpContext context, ChallengePayload payload)
    {
        var serialized = payload.Serialize();
        context.Response.Cookies.Append(
            ChallengeCookieName,
            serialized,
            CookieMapping.ToEphemeralCookieOptions("/", ChallengeCookieTtl, _timeProvider.GetUtcNow()));
    }

    private static void AppendQuery(StringBuilder sb, string key, string value)
    {
        sb.Append('&').Append(Uri.EscapeDataString(key)).Append('=').Append(Uri.EscapeDataString(value));
    }
}

/// <summary>
/// Payload written to the one-shot challenge cookie by
/// <see cref="ChallengeHelper.BuildLoginUrlAsync"/>. Consumer
/// callback handlers read it via <see cref="ChallengeHelper.ConsumeChallengeCookie"/>.
/// </summary>
/// <param name="Client">OIDC client name.</param>
/// <param name="CodeVerifier">PKCE code_verifier to replay at the token endpoint.</param>
/// <param name="State">Anti-CSRF state; match against the <c>state</c> query arg.</param>
/// <param name="Nonce">id-token nonce; match against the id-token <c>nonce</c> claim.</param>
/// <param name="ReturnUrl">Post-login return URL chosen by the caller.</param>
public sealed record ChallengePayload(
    string Client,
    string CodeVerifier,
    string State,
    string Nonce,
    string ReturnUrl)
{
    /// <summary>Serializes the payload into the on-wire cookie string.</summary>
    /// <remarks>
    /// Pipe-separated format keeps the encoding reflection-free and
    /// auditable at a glance. Each field is assumed to be url-safe
    /// (base64url for the random components, a local URL for
    /// <see cref="ReturnUrl"/>); consumers must validate
    /// <see cref="ReturnUrl"/> against their own open-redirect policy.
    /// </remarks>
    public string Serialize()
    {
        return string.Concat(
            Client, '|',
            CodeVerifier, '|',
            State, '|',
            Nonce, '|',
            Uri.EscapeDataString(ReturnUrl));
    }

    /// <summary>Parses the on-wire cookie string back into a <see cref="ChallengePayload"/>.</summary>
    public static bool TryParse(string raw, out ChallengePayload? payload)
    {
        payload = null;
        if (string.IsNullOrEmpty(raw))
        {
            return false;
        }

        var parts = raw.Split('|', count: 5);
        if (parts.Length != 5)
        {
            return false;
        }

        payload = new ChallengePayload(
            Client: parts[0],
            CodeVerifier: parts[1],
            State: parts[2],
            Nonce: parts[3],
            ReturnUrl: Uri.UnescapeDataString(parts[4]));
        return true;
    }
}
