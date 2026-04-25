namespace Kun.Gateway.Configuration;

/// <summary>
/// Configuration options for the Kun Gateway.
/// Controls external JWT validation, internal S2S JWT issuance, and key paths.
/// </summary>
public sealed class KunGatewayOptions
{
    /// <summary>
    /// The issuer of the external IdP-issued JWT (e.g. <c>https://login.example.com</c>).
    /// </summary>
    public required string ExternalIssuer { get; set; }

    /// <summary>
    /// The expected audience of the external IdP-issued JWT.
    /// </summary>
    public required string ExternalAudience { get; set; }

    /// <summary>
    /// The JWKS endpoint URI of the external IdP, used to fetch public keys for validating incoming JWTs.
    /// </summary>
    public required string ExternalJwksUri { get; set; }

    /// <summary>
    /// The issuer claim placed in the minted internal S2S JWT. Defaults to <c>kun-gateway</c>.
    /// </summary>
    public string InternalIssuer { get; set; } = "kun-gateway";

    /// <summary>
    /// The audience claim placed in the minted internal S2S JWT. Defaults to <c>kun-internal</c>.
    /// </summary>
    public string InternalAudience { get; set; } = "kun-internal";

    /// <summary>
    /// Lifetime of the minted internal S2S JWT. Defaults to 60 seconds per zero-trust TTL policy.
    /// </summary>
    public TimeSpan InternalTokenLifetime { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Absolute path to the PEM-encoded RSA private key file used to sign internal S2S JWTs.
    /// </summary>
    public required string SigningKeyPath { get; set; }

    /// <summary>
    /// Absolute path to the PEM-encoded RSA public key file exposed via the JWKS endpoint.
    /// </summary>
    public required string PublicKeyPath { get; set; }

    /// <summary>
    /// When <see langword="true"/> (default), the gateway emits a <c>trace_id</c> claim in the
    /// minted S2S JWT if an active OpenTelemetry <see cref="System.Diagnostics.Activity"/> with a
    /// valid <see cref="System.Diagnostics.ActivityTraceId"/> is present on the request.
    /// Set to <see langword="false"/> to suppress trace propagation via JWT (e.g. for compliance).
    /// </summary>
    public bool EmitTraceIdClaim { get; set; } = true;

    /// <summary>
    /// When <see langword="true"/>, the gateway exposes the internal JWKS endpoint
    /// <c>/internal/.well-known/jwks.json</c> in non-Development environments. Defaults to
    /// <see langword="false"/> so the internal endpoint returns 404 in production unless the
    /// operator deliberately opts in. The public endpoint <c>/.well-known/jwks.json</c>
    /// remains always-on; this option only gates the internal-network variant.
    /// </summary>
    /// <remarks>
    /// Keep <see langword="false"/> in internet-facing Kubernetes deployments and rely on
    /// NetworkPolicy / service-mesh egress rules to isolate the internal route. Flip to
    /// <see langword="true"/> only when JWKS fetching from internal pods must bypass the
    /// public path (e.g. rate-limiting sidecars that reshape external traffic).
    /// </remarks>
    public bool ExposeInternalJwks { get; set; }
}
