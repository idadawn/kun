namespace Kun.Security.S2SJwt;

/// <summary>
/// Configuration for S2S JWT issuance and verification using ES256 (ECDSA P-256).
/// Per the zero-trust policy, ES256 is the required algorithm for legacy S2S tokens.
/// </summary>
[Obsolete("Use Kun.Gateway.S2sTokenIssuer and Kun.Security service authentication.", DiagnosticId = "KUNS2S001")]
public sealed class S2SJwtOptions
{
    /// <summary>ECDSA P-256 private key in PEM format for signing tokens.</summary>
    public required string SigningKeyPem { get; set; }

    /// <summary>Key identifier for the current signing key.</summary>
    public required string KeyId { get; set; }

    /// <summary>Previous ECDSA P-256 private key PEM for graceful key rotation. Optional.</summary>
    public string? PreviousSigningKeyPem { get; set; }

    /// <summary>Key identifier for the previous signing key. Required when <see cref="PreviousSigningKeyPem"/> is set.</summary>
    public string? PreviousKeyId { get; set; }

    /// <summary>Token issuer. Must be <c>kun-gateway</c> per contract.</summary>
    public string Issuer { get; set; } = "kun-gateway";

    /// <summary>
    /// Default token lifetime. Defaults to 300 seconds (5 minutes), the upper bound of the
    /// whitepaper §9.6 range ("60 秒到 5 分钟"). Individual calls to
    /// <see cref="IS2SJwtIssuer.IssueAsync(S2SJwtIssueRequest, System.Threading.CancellationToken)"/>
    /// may tighten this via <see cref="S2SJwtIssueRequest.Lifetime"/>.
    /// </summary>
    public int DefaultLifetimeSeconds { get; set; } = 300;
}
