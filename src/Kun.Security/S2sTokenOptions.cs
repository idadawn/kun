namespace Kun.Security;

/// <summary>
/// Configuration for S2S JWT issuance and validation.
/// Algorithm is always asymmetric (RS256/ES256) — HS256 is rejected per zero-trust policy.
/// </summary>
public sealed record S2sTokenOptions
{
    /// <summary>Token issuer (e.g. <c>https://kun-gateway.internal</c>).</summary>
    public required string Issuer { get; init; }

    /// <summary>Expected audience. Defaults to <c>kun-internal</c>.</summary>
    public string Audience { get; init; } = "kun-internal";

    /// <summary>Token lifetime. Defaults to 60 seconds per zero-trust TTL policy.</summary>
    public TimeSpan Lifetime { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Signing algorithm. Must be an asymmetric algorithm (RS256 or ES256).
    /// HS256 is explicitly forbidden — tokens signed with HS256 are rejected.
    /// </summary>
    public string SigningAlgorithm { get; init; } = "RS256";

    /// <summary>
    /// Absolute or relative URI to the JWKS endpoint on the internal network.
    /// Used by downstream services to fetch public keys for validation.
    /// </summary>
    public string? JwksUri { get; init; }

    /// <summary>
    /// Additional valid issuers for multi-issuer scenarios (e.g. blue/green gateway deployments).
    /// <see cref="Issuer"/> is always included; this list supplements it.
    /// </summary>
    public IReadOnlyList<string>? ValidIssuers { get; init; }
}
