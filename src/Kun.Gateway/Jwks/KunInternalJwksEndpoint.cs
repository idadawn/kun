using Kun.Gateway.Configuration;

namespace Kun.Gateway.Jwks;

/// <summary>
/// Extension methods for mapping the <b>internal-network</b> JWKS endpoint, separate from the
/// always-on public <c>/.well-known/jwks.json</c> route.
/// </summary>
/// <remarks>
/// <para>
/// The public endpoint (see <c>JwksEndpoint.MapKunJwks</c>) is required for any consumer — inside
/// or outside the cluster — to validate S2S tokens, and stays unconditionally enabled. The internal
/// endpoint at <c>/internal/.well-known/jwks.json</c> is an opt-in convenience for downstream
/// services that talk to the gateway through a NetworkPolicy-isolated interface and want a stable
/// well-known path that does NOT share lifecycle with the public route (e.g. for separate
/// rate-limiting or egress controls).
/// </para>
/// <para>
/// Gating policy: the internal route is reachable only when either (a) the host runs in the
/// <c>Development</c> environment, or (b) <see cref="KunGatewayOptions.ExposeInternalJwks"/> is
/// explicitly set to <see langword="true"/>. Any other environment returns 404, so forgetting to
/// set the flag in production fails closed.
/// </para>
/// </remarks>
public static class KunInternalJwksEndpoint
{
    /// <summary>Default path of the gated internal JWKS route.</summary>
    public const string DefaultPattern = "/internal/.well-known/jwks.json";

    /// <summary>
    /// Maps the internal JWKS endpoint, gated on host environment + options.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="pattern">Override the default <c>/internal/.well-known/jwks.json</c> path.</param>
    /// <param name="additionalGate">
    /// Optional predicate evaluated on each request after the environment / <see cref="KunGatewayOptions.ExposeInternalJwks"/>
    /// check passes. Return <see langword="false"/> to respond 404 (e.g. restrict to RFC1918 peers
    /// when no NetworkPolicy is in place). When <see langword="null"/>, no additional gating is applied.
    /// </param>
    /// <returns>The <see cref="IEndpointConventionBuilder"/> for the mapped route.</returns>
    public static IEndpointConventionBuilder MapKunInternalJwks(
        this IEndpointRouteBuilder endpoints,
        string pattern = DefaultPattern,
        Func<HttpContext, bool>? additionalGate = null)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);

        return endpoints.MapGet(pattern, (
            HttpContext httpContext,
            Microsoft.Extensions.Options.IOptions<KunGatewayOptions> options,
            IHostEnvironment environment,
            JwksResponseBuilder builder) =>
        {
            var opts = options.Value;

            // Fail closed: in any non-Development environment, require an explicit opt-in.
            // Operators who forget the flag get a 404, not an accidental public-leak of the
            // internal path.
            if (!environment.IsDevelopment() && !opts.ExposeInternalJwks)
            {
                return Results.NotFound();
            }

            // Caller-supplied gate (defence-in-depth). Runs after the env/options check so
            // dev-mode is already permissive; typical use is a loopback + RFC1918 allow-list
            // for production deployments that can't rely on NetworkPolicy.
            if (additionalGate is not null && !additionalGate(httpContext))
            {
                return Results.NotFound();
            }

            var keySet = builder.Build(opts.PublicKeyPath);
            return Results.Ok(keySet);
        });
    }
}
