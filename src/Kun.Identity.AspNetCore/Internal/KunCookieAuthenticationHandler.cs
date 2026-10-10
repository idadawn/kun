using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kun.Identity.AspNetCore.Internal;

/// <summary>Bridges the middleware's verified opaque-cookie principal into ASP.NET authorization/challenge.</summary>
internal sealed class KunCookieAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        => Task.FromResult(Context.User.Identity?.IsAuthenticated == true && Context.User.Identity.AuthenticationType == SessionPrincipalFactory.SchemeName
            ? AuthenticateResult.Success(new AuthenticationTicket(Context.User, Scheme.Name)) : AuthenticateResult.NoResult());
}
