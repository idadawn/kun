using Kun.Identity.Configuration;
using Microsoft.AspNetCore.Http;

namespace Kun.Identity.AspNetCore.Internal;

/// <summary>
/// Maps the framework-neutral <see cref="KunCookieAuthOptions"/> onto
/// the ASP.NET Core cookie primitives. Kept in a single file so the
/// options → cookie mapping lives at exactly one callsite.
/// </summary>
internal static class CookieMapping
{
    public static CookieOptions ToCookieOptions(KunCookieAuthOptions options, DateTimeOffset expiresAt)
    {
        return new CookieOptions
        {
            Domain = options.Domain,
            Path = options.Path,
            HttpOnly = options.HttpOnly,
            Secure = options.Secure == KunCookieSecurePolicy.Always
                || options.Secure == KunCookieSecurePolicy.SameAsRequest,
            SameSite = options.SameSite switch
            {
                KunSameSiteMode.Unspecified => SameSiteMode.Unspecified,
                KunSameSiteMode.None => SameSiteMode.None,
                KunSameSiteMode.Lax => SameSiteMode.Lax,
                KunSameSiteMode.Strict => SameSiteMode.Strict,
                _ => SameSiteMode.Lax,
            },
            Expires = expiresAt,
        };
    }

    public static CookieOptions ToEphemeralCookieOptions(string path, TimeSpan lifetime, DateTimeOffset now)
    {
        // Challenge cookie holding PKCE verifier / state / nonce. Must
        // survive the cross-site redirect from the IdP back to our
        // /auth/callback, so SameSite=None + Secure=true is required.
        return new CookieOptions
        {
            Path = path,
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.None,
            Expires = now.Add(lifetime),
        };
    }
}
