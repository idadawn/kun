namespace Kun.Security.S2SJwt;

/// <summary>
/// Configuration for S2S JWT issuance and verification using ES256 (ECDSA P-256).
/// Per the zero-trust policy, ES256 is the required algorithm for NeuroAgent S2S tokens.
/// </summary>
public sealed class S2SJwtOptions
{
    /// <summary>ECDSA P-256 private key in PEM format for signing tokens.</summary>
    public required string SigningKeyPem { get; init; }

    /// <summary>Key identifier for the current signing key.</summary>
    public required string KeyId { get; init; }

    /// <summary>Previous ECDSA P-256 private key PEM for graceful key rotation. Optional.</summary>
    public string? PreviousSigningKeyPem { get; init; }

    /// <summary>Key identifier for the previous signing key. Required when <see cref="PreviousSigningKeyPem"/> is set.</summary>
    public string? PreviousKeyId { get; init; }

    /// <summary>Token issuer. Must be <c>neuroagent-edge</c> per contract.</summary>
    public string Issuer { get; init; } = "neuroagent-edge";

    /// <summary>
    /// Default token lifetime. Defaults to 900 seconds (15 minutes) per SAD v3.0 §12.
    /// Individual calls to <see cref="IS2SJwtIssuer.IssueAsync"/> can override via
    /// <see cref="S2SJwtIssueRequest.Lifetime"/>.
    /// </summary>
    public int DefaultLifetimeSeconds { get; init; } = 900;
}
