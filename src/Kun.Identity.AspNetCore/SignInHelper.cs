using System.Security.Claims;
using System.Security.Cryptography;
using Kun.Identity.Abstractions;
using Kun.Identity.Abstractions.Session;
using Kun.Identity.AspNetCore.Internal;
using Kun.Identity.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Kun.Identity.AspNetCore;

/// <summary>
/// Turns a successful OIDC code-exchange into a signed-in browser
/// session: writes the <see cref="SessionData"/> into the configured
/// <see cref="ISessionStore"/> and emits the opaque session cookie on
/// the current response.
/// </summary>
public sealed class SignInHelper
{
    private readonly ISessionStore _store;
    private readonly IJwtValidator _idTokenValidator;
    private readonly KunCookieAuthOptions _options;
    private readonly TimeProvider _timeProvider;

    /// <summary>Creates a new <see cref="SignInHelper"/>.</summary>
    public SignInHelper(
        ISessionStore store,
        IJwtValidator idTokenValidator,
        IOptions<KunCookieAuthOptions> options,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(idTokenValidator);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _store = store;
        _idTokenValidator = idTokenValidator;
        _options = options.Value;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Writes <paramref name="data"/> to the session store and emits
    /// the cookie on <paramref name="context"/>. Returns the newly
    /// minted opaque session id (256 bits of entropy, base64url-encoded).
    /// </summary>
    public async Task<string> SignInAsync(HttpContext context, SessionData data, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(data);

        var sessionId = CreateSessionId();
        await _store.SetAsync(sessionId, data, _options.SessionTtl, cancellationToken).ConfigureAwait(false);

        var expiresAt = _timeProvider.GetUtcNow().Add(_options.SessionTtl);
        context.Response.Cookies.Append(
            _options.CookieName,
            sessionId,
            CookieMapping.ToCookieOptions(_options, expiresAt));

        return sessionId;
    }

    /// <summary>
    /// Convenience overload that validates <paramref name="tokens"/>.IdToken
    /// through <see cref="IJwtValidator"/>, builds <see cref="SessionData"/>
    /// from the resulting claims, and delegates to
    /// <see cref="SignInAsync(HttpContext, SessionData, CancellationToken)"/>.
    /// </summary>
    /// <remarks>
    /// The id-token is validated — not merely parsed — before any
    /// claim is trusted. An id-token that fails validation throws
    /// <see cref="InvalidOperationException"/>; callers decide
    /// whether to surface a 400 or a 401.
    /// </remarks>
    public async Task<string> SignInAsync(HttpContext context, OidcTokenResponse tokens, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(tokens);
        if (string.IsNullOrEmpty(tokens.IdToken))
        {
            throw new InvalidOperationException("OIDC token response is missing the id-token; cannot materialise a session without a verified identity.");
        }

        var principal = await _idTokenValidator.ValidateAsync(tokens.IdToken, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("OIDC id-token failed validation.");

        var data = BuildSessionData(principal, tokens, _timeProvider.GetUtcNow());
        return await SignInAsync(context, data, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Removes the cookie and deletes the corresponding session entry.
    /// Safe to call on anonymous requests — a no-op when no cookie is
    /// present.
    /// </summary>
    public async Task SignOutAsync(HttpContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var sid = context.Request.Cookies[_options.CookieName];
        if (!string.IsNullOrEmpty(sid))
        {
            await _store.DeleteAsync(sid, cancellationToken).ConfigureAwait(false);
        }

        context.Response.Cookies.Delete(_options.CookieName, new CookieOptions
        {
            Path = _options.Path,
            Domain = _options.Domain,
        });
    }

    internal static SessionData BuildSessionData(ClaimsPrincipal principal, OidcTokenResponse tokens, DateTimeOffset now)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? principal.FindFirstValue("sub")
            ?? throw new InvalidOperationException("Validated id-token is missing a subject (sub / nameid) claim.");

        var tenantId = principal.FindFirstValue(KunCurrentUser.TenantIdClaimType);
        var email = principal.FindFirstValue(ClaimTypes.Email) ?? principal.FindFirstValue("email");
        var name = principal.FindFirstValue(ClaimTypes.Name) ?? principal.FindFirstValue("name");

        var roles = new List<string>(capacity: 4);
        foreach (var claim in principal.FindAll(ClaimTypes.Role))
        {
            roles.Add(claim.Value);
        }
        foreach (var claim in principal.FindAll("roles"))
        {
            roles.Add(claim.Value);
        }

        return new SessionData(
            UserId: userId,
            TenantId: tenantId,
            Email: email,
            Name: name,
            Roles: roles,
            AccessToken: tokens.AccessToken,
            RefreshToken: tokens.RefreshToken,
            ExpiresAt: now.AddSeconds(tokens.ExpiresIn));
    }

    private static string CreateSessionId()
    {
        // 256 bits of entropy, url-safe encoding (no padding, no '+' / '/').
        Span<byte> buffer = stackalloc byte[32];
        RandomNumberGenerator.Fill(buffer);
        return Convert.ToBase64String(buffer)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }
}
