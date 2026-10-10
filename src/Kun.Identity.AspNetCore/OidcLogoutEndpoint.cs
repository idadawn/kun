using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kun.Identity.AspNetCore;

/// <summary>Back-channel logout mapping; authentication comes from the signed provider token, not a browser cookie.</summary>
public static class OidcLogoutEndpoint
{
    private static readonly Action<ILogger, string, Exception?> s_rejected = LoggerMessage.Define<string>(LogLevel.Warning, new EventId(30, "LogoutRejected"), "Back-channel logout rejected; error_type={ErrorType}.");

    /// <summary>Maps the form-post logout endpoint for one registered provider. Returns 200 on success/replay and 400 on rejection.</summary>
    public static IEndpointConventionBuilder MapKunBackChannelLogout(this IEndpointRouteBuilder endpoints, string pattern, string clientName)
    {
        ArgumentNullException.ThrowIfNull(endpoints); ArgumentException.ThrowIfNullOrWhiteSpace(pattern); ArgumentException.ThrowIfNullOrWhiteSpace(clientName);
        return endpoints.MapPost(pattern, (RequestDelegate)(async context =>
        {
            context.Response.Headers.CacheControl = "no-store";
            if (!context.Request.HasFormContentType) { context.Response.StatusCode = StatusCodes.Status400BadRequest; return; }
            try
            {
                var form = await context.Request.ReadFormAsync(context.RequestAborted).ConfigureAwait(false);
                if (!form.TryGetValue("logout_token", out var token) || token.Count != 1 || string.IsNullOrEmpty(token[0]))
                { context.Response.StatusCode = StatusCodes.Status400BadRequest; return; }
                await context.RequestServices.GetRequiredService<OidcLogoutService>().HandleAsync(clientName, token[0]!, context.RequestAborted).ConfigureAwait(false);
                context.Response.StatusCode = StatusCodes.Status200OK;
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                s_rejected(context.RequestServices.GetRequiredService<ILogger<OidcLogoutService>>(), error.GetType().Name, null);
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
            }
        })).AllowAnonymous().DisableAntiforgery();
    }
}
