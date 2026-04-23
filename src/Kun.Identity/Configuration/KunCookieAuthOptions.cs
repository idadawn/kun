namespace Kun.Identity.Configuration;

/// <summary>
/// Options for the cookie session layer wired by
/// <c>AddKunIdentity(id =&gt; id.AddCookieSession(...))</c>. The defaults
/// encode the zero-trust baseline <c>Kun.Gateway</c> already applies to
/// outbound cookies.
/// </summary>
/// <remarks>
/// This options type lives in <c>Kun.Identity</c> (not
/// <c>Kun.Identity.AspNetCore</c>) so non-ASP.NET-Core hosts — worker
/// services that replay a signed-in request, integration tests that
/// synthesize a session — can configure it without pulling the ASP.NET
/// Core dependency. <see cref="SameSite"/> and <see cref="Secure"/>
/// use Kun-owned enums (<see cref="KunSameSiteMode"/>,
/// <see cref="KunCookieSecurePolicy"/>); the middleware in
/// <c>Kun.Identity.AspNetCore</c> maps them to the equivalent ASP.NET
/// Core values at registration time.
/// </remarks>
public class KunCookieAuthOptions
{
    /// <summary>Session cookie name. Default <c>kun.sid</c>.</summary>
    public string CookieName { get; set; } = "kun.sid";

    /// <summary>
    /// Cookie <c>Domain</c>. Leave <see langword="null"/> to scope the
    /// cookie to the exact host that set it (recommended for
    /// multi-tenant isolation; only override when the session must be
    /// shared across subdomains).
    /// </summary>
    public string? Domain { get; set; }

    /// <summary>Cookie <c>Path</c>. Default <c>/</c>.</summary>
    public string Path { get; set; } = "/";

    /// <summary>
    /// Emit the cookie with <c>HttpOnly</c>. Default <see langword="true"/>
    /// and strongly recommended to stay that way — JavaScript must not
    /// be able to read the session id.
    /// </summary>
    public bool HttpOnly { get; set; } = true;

    /// <summary>
    /// <c>SameSite</c> policy. Default <see cref="KunSameSiteMode.Lax"/>
    /// so top-level redirects from the IdP's /authorize response still
    /// carry the cookie, while third-party iframes / XHR do not.
    /// </summary>
    public KunSameSiteMode SameSite { get; set; } = KunSameSiteMode.Lax;

    /// <summary>
    /// Whether the cookie is emitted only over HTTPS. Default
    /// <see cref="KunCookieSecurePolicy.Always"/>; drop to
    /// <see cref="KunCookieSecurePolicy.SameAsRequest"/> only for
    /// development against <c>http://localhost</c>.
    /// </summary>
    public KunCookieSecurePolicy Secure { get; set; } = KunCookieSecurePolicy.Always;

    /// <summary>
    /// Absolute session lifetime. Default 8 hours. The cookie
    /// <c>Max-Age</c> and the <see cref="Abstractions.Session.ISessionStore"/>
    /// entry TTL are both bound to this value at sign-in.
    /// </summary>
    public TimeSpan SessionTtl { get; set; } = TimeSpan.FromHours(8);

    /// <summary>
    /// When <see langword="true"/>, authenticated requests extend the
    /// session on both the cookie and the store (matching the
    /// <c>SessionTtl</c> window from the current instant). Default
    /// <see langword="true"/>.
    /// </summary>
    public bool SlidingExpiration { get; set; } = true;
}

/// <summary>
/// Secure-policy for <see cref="KunCookieAuthOptions.Secure"/>. Mirrors
/// ASP.NET Core's <c>CookieSecurePolicy</c> shape without taking a
/// dependency on that package from the core abstractions.
/// </summary>
public enum KunCookieSecurePolicy
{
    /// <summary>Emit the cookie only over HTTPS (default in production).</summary>
    Always = 0,

    /// <summary>Emit the cookie over either scheme (development only).</summary>
    None = 1,

    /// <summary>Match the scheme of the current request.</summary>
    SameAsRequest = 2,
}

/// <summary>
/// <c>SameSite</c> policy for <see cref="KunCookieAuthOptions.SameSite"/>.
/// Mirrors ASP.NET Core's <c>SameSiteMode</c> shape without taking a
/// dependency on that package from the core abstractions.
/// </summary>
public enum KunSameSiteMode
{
    /// <summary>No <c>SameSite</c> attribute emitted (browser default).</summary>
    Unspecified = -1,

    /// <summary>
    /// <c>SameSite=None</c>. Cookie is sent on cross-site requests; must
    /// be paired with <see cref="KunCookieSecurePolicy.Always"/>.
    /// </summary>
    None = 0,

    /// <summary>
    /// <c>SameSite=Lax</c>. Cookie is sent on top-level navigation but not
    /// on cross-site subrequests. Recommended default for OIDC code flow.
    /// </summary>
    Lax = 1,

    /// <summary>
    /// <c>SameSite=Strict</c>. Cookie is only sent on same-site requests —
    /// blocks even top-level redirects from the IdP callback, so usually
    /// only appropriate for post-login navigation cookies.
    /// </summary>
    Strict = 2,
}
