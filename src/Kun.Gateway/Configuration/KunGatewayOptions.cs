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
    /// Compatibility allowlist option. Only canonical scope/session/revision/project/plugin field names are accepted;
    /// the canonical field set is fixed and does not require an allowlist. Arbitrary application claims are rejected.
    /// </summary>
    public string[] ForwardedClaimTypes { get; set; } = [];

    /// <summary>
    /// Absolute path to the PEM-encoded RSA private key file used to sign internal S2S JWTs.
    /// </summary>
    public required string SigningKeyPath { get; set; }

    /// <summary>
    /// Legacy public PEM path; generated in Development and used by standalone JWKS mapping.
    /// With AddKunGateway, JWKS is derived from the active signing key and overlap key ring.
    /// </summary>
    public required string PublicKeyPath { get; set; }

    /// <summary>
    /// Retained for configuration compatibility; no longer changes the closed JWT payload.
    /// Trace context is propagated through standard HTTP headers.
    /// </summary>
    public bool EmitTraceIdClaim { get; set; }

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

    /// <summary>允许转发客户端地址与协议的代理 IP；缺省不信任任何转发头。</summary>
    public string[] TrustedProxyAddresses { get; set; } = [];

    /// <summary>允许转发客户端地址与协议的代理 CIDR 网段；应只包含实际代理。</summary>
    public string[] TrustedProxyNetworks { get; set; } = [];

    /// <summary>最多接受的可信代理跳数，默认一跳。</summary>
    public int ForwardLimit { get; set; } = 1;

    /// <summary>轮换后旧公钥的重叠发布时长，默认五分钟；消费者仍须执行令牌寿命验证。</summary>
    public TimeSpan KeyOverlapPeriod { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>检查私钥文件变更的最短间隔，默认一秒；零表示每次签发或获取 JWKS 都检查。</summary>
    public TimeSpan KeyReloadInterval { get; set; } = TimeSpan.FromSeconds(1);
}
