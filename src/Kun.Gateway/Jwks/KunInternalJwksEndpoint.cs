using System.Security.Cryptography;
using Kun.Gateway.Configuration;
using Kun.Gateway.Endpoints;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

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
    /// <returns>The <see cref="IEndpointConventionBuilder"/> for the mapped route.</returns>
    public static IEndpointConventionBuilder MapKunInternalJwks(
        this IEndpointRouteBuilder endpoints,
        string pattern = DefaultPattern)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);

        return endpoints.MapGet(pattern, (
            IOptions<KunGatewayOptions> options,
            IHostEnvironment environment) =>
        {
            var opts = options.Value;

            // Fail closed: in any non-Development environment, require an explicit opt-in.
            // Operators who forget the flag get a 404, not an accidental public-leak of the
            // internal path.
            if (!environment.IsDevelopment() && !opts.ExposeInternalJwks)
            {
                return Results.NotFound();
            }

            var pem = File.ReadAllText(opts.PublicKeyPath);
            using var rsa = RSA.Create();
            rsa.ImportFromPem(pem);

            var rsaKey = new RsaSecurityKey(rsa);
            var jwk = JsonWebKeyConverter.ConvertFromRSASecurityKey(rsaKey);

            var modulusBytes = rsa.ExportParameters(false).Modulus ?? [];
            var hash = SHA256.HashData(modulusBytes);
            jwk.KeyId = Convert.ToBase64String(hash)[..16];
            jwk.Use = "sig";
            jwk.Alg = "RS256";

            var keySet = new JwksResponse
            {
                Keys = [new JwkDto(jwk)],
            };

            return Results.Ok(keySet);
        });
    }
}
