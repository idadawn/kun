using Kun.Identity.Abstractions.Session;
using Kun.Identity.AspNetCore.Internal;
using Kun.Identity.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kun.Identity.AspNetCore;

/// <summary>
/// Middleware that hydrates <c>HttpContext.User</c> from a
/// <see cref="ISessionStore"/> entry keyed by the session cookie.
/// Requests without a valid session flow through anonymously; the
/// middleware never writes a 401 itself — downstream authorization
/// policies are responsible for that.
/// </summary>
public sealed partial class KunCookieAuthMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ISessionStore _store;
    private readonly KunCookieAuthOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<KunCookieAuthMiddleware> _logger;

    /// <summary>Creates the middleware.</summary>
    public KunCookieAuthMiddleware(
        RequestDelegate next,
        ISessionStore store,
        IOptions<KunCookieAuthOptions> options,
        TimeProvider timeProvider,
        ILogger<KunCookieAuthMiddleware> logger)
    {
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);
        _next = next;
        _store = store;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>Entry point called by the ASP.NET Core pipeline.</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var cookie = context.Request.Cookies[_options.CookieName];
        if (string.IsNullOrEmpty(cookie))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        var ct = context.RequestAborted;
        SessionData? data;
        try
        {
            data = await _store.GetAsync(cookie, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.SessionLookupFailed(_logger, cookie, ex);
            await _next(context).ConfigureAwait(false);
            return;
        }

        if (data is null)
        {
            // Cookie references an unknown or expired session — evict
            // the stale cookie so the client stops presenting it.
            context.Response.Cookies.Delete(_options.CookieName, new CookieOptions
            {
                Path = _options.Path,
                Domain = _options.Domain,
            });
            await _next(context).ConfigureAwait(false);
            return;
        }

        context.User = SessionPrincipalFactory.FromSession(data);

        if (_options.SlidingExpiration)
        {
            var now = _timeProvider.GetUtcNow();
            var newExpiry = now.Add(_options.SessionTtl);
            try
            {
                await _store.SetAsync(cookie, data, _options.SessionTtl, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Never fail the request on a sliding-refresh failure;
                // the session is still valid for the remainder of its
                // original TTL.
                Log.SessionSlideFailed(_logger, cookie, ex);
            }

            context.Response.Cookies.Append(
                _options.CookieName,
                cookie,
                CookieMapping.ToCookieOptions(_options, newExpiry));
        }

        await _next(context).ConfigureAwait(false);
    }

    private static partial class Log
    {
        [LoggerMessage(EventId = 1, Level = LogLevel.Warning,
            Message = "Kun cookie session lookup failed for sid={SessionId}; request continues anonymously.")]
        public static partial void SessionLookupFailed(ILogger logger, string sessionId, Exception ex);

        [LoggerMessage(EventId = 2, Level = LogLevel.Warning,
            Message = "Kun cookie session sliding-refresh failed for sid={SessionId}; original TTL still applies.")]
        public static partial void SessionSlideFailed(ILogger logger, string sessionId, Exception ex);
    }
}

/// <summary>
/// <c>IApplicationBuilder</c> extensions for the Kun cookie middleware.
/// </summary>
public static class KunCookieAuthApplicationBuilderExtensions
{
    /// <summary>
    /// Inserts <see cref="KunCookieAuthMiddleware"/> into the pipeline.
    /// Call before any endpoint / authorization middleware that depends
    /// on <c>HttpContext.User</c>.
    /// </summary>
    public static IApplicationBuilder UseKunCookieAuth(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.UseMiddleware<KunCookieAuthMiddleware>();
    }
}
