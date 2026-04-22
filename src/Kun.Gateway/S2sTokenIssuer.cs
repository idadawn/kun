using System.Diagnostics;
using System.Security.Claims;
using System.Security.Cryptography;
using Kun.Gateway.Configuration;
using Kun.Gateway.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Kun.Gateway;

/// <summary>
/// Issues short-lived internal S2S JWTs signed with the gateway's RSA private key.
/// The issued token copies <c>sub</c>, <c>tenant_id</c>, and <c>roles</c> claims from
/// the authenticated external principal (after the configured
/// <see cref="IS2sPrincipalResolver"/> runs), sets <c>aud=kun-internal</c> and a 60-second TTL.
/// When <see cref="KunGatewayOptions.EmitTraceIdClaim"/> is <see langword="true"/> and an active
/// <see cref="Activity"/> with a valid trace id is present, a <c>trace_id</c> claim is also emitted.
/// </summary>
public sealed class S2sTokenIssuer
{
    private readonly RsaSecurityKey _signingKey;
    private readonly SigningCredentials _credentials;
    private readonly KunGatewayOptions _options;
    private readonly IS2sPrincipalResolver _resolver;
    private readonly IHttpContextAccessor? _httpContextAccessor;

    /// <summary>
    /// Initializes the issuer with an explicit principal resolver. Prefer this ctor —
    /// the 3-arg overload is kept for binary compatibility but is deprecated.
    /// </summary>
    /// <param name="options">Gateway configuration options.</param>
    /// <param name="env">Host environment used to differentiate Development from Production.</param>
    /// <param name="resolver">Principal resolver (use <see cref="NoOpS2sPrincipalResolver.Instance"/> for the identity transform).</param>
    /// <param name="httpContextAccessor">
    /// Optional HTTP context accessor used to confirm a live HTTP request is in scope before
    /// emitting the <c>trace_id</c> claim (M3 guard). When <see langword="null"/>, trace-id
    /// emission is suppressed.
    /// </param>
    public S2sTokenIssuer(
        IOptions<KunGatewayOptions> options,
        IHostEnvironment env,
        IS2sPrincipalResolver resolver,
        IHttpContextAccessor? httpContextAccessor = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(resolver);
        _options = options.Value;
        _resolver = resolver;
        _httpContextAccessor = httpContextAccessor;

        var rsa = RSA.Create();
        if (File.Exists(_options.SigningKeyPath))
        {
            rsa.ImportFromPem(File.ReadAllText(_options.SigningKeyPath));
        }
        else if (env.IsDevelopment())
        {
            rsa.KeySize = 2048;
            EnsureDirectory(_options.SigningKeyPath);
            File.WriteAllText(_options.SigningKeyPath, rsa.ExportRSAPrivateKeyPem());
            EnsureDirectory(_options.PublicKeyPath);
            File.WriteAllText(_options.PublicKeyPath, rsa.ExportRSAPublicKeyPem());
        }
        else
        {
            throw new InvalidOperationException(
                $"S2sTokenIssuer: signing key not found at '{_options.SigningKeyPath}'. " +
                "Production deployments must pre-provision the RSA key pair. " +
                "For development, set ASPNETCORE_ENVIRONMENT=Development.");
        }

        _signingKey = new RsaSecurityKey(rsa);
        _credentials = new SigningCredentials(_signingKey, SecurityAlgorithms.RsaSha256);
    }

    /// <summary>
    /// Backward-compatible overload without an <see cref="IS2sPrincipalResolver"/> — falls back
    /// to <see cref="NoOpS2sPrincipalResolver.Instance"/>. Kept for one release so existing
    /// consumers who wired <see cref="S2sTokenIssuer"/> manually continue to compile.
    /// </summary>
    /// <param name="options">Gateway configuration options.</param>
    /// <param name="env">Host environment used to differentiate Development from Production.</param>
    /// <param name="httpContextAccessor">Optional HTTP context accessor (M3 guard for trace-id emission).</param>
    [Obsolete(
        "Use the 4-arg constructor that accepts an IS2sPrincipalResolver. " +
        "The 3-arg overload will be removed in 0.2.0.",
        DiagnosticId = "KUN0002",
        UrlFormat = "https://github.com/idadawn/kun/issues")]
    public S2sTokenIssuer(
        IOptions<KunGatewayOptions> options,
        IHostEnvironment env,
        IHttpContextAccessor? httpContextAccessor)
        : this(options, env, NoOpS2sPrincipalResolver.Instance, httpContextAccessor)
    {
    }

    private static void EnsureDirectory(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }

    /// <summary>
    /// Issues an internal S2S JWT for the given external <paramref name="externalPrincipal"/>.
    /// The configured <see cref="IS2sPrincipalResolver"/> runs first to produce the effective
    /// principal, then <c>sub</c>, <c>tenant_id</c>, and all <c>roles</c> claims are copied
    /// into the new token.
    /// </summary>
    /// <param name="externalPrincipal">The authenticated principal from the external IdP JWT.</param>
    /// <returns>A signed RS256 JWT string.</returns>
    public string IssueFor(ClaimsPrincipal externalPrincipal)
    {
        ArgumentNullException.ThrowIfNull(externalPrincipal);

        // Resolver is expected to be cheap (cache-backed or CompletedTask-returning). Blocking
        // here keeps the sync public API while letting consumers plug an async lookup if they
        // understand the cost.
        var principal = _resolver
            .ResolveAsync(externalPrincipal, CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        ArgumentNullException.ThrowIfNull(principal);

        var now = DateTime.UtcNow;
        var claims = new List<Claim>();

        // Copy sub claim
        var sub = principal.FindFirst(JwtRegisteredClaimNames.Sub)
            ?? principal.FindFirst(ClaimTypes.NameIdentifier);
        if (sub is not null)
            claims.Add(new Claim(JwtRegisteredClaimNames.Sub, sub.Value));

        // Copy tenant_id claim
        var tenantId = principal.FindFirst("tenant_id");
        if (tenantId is not null)
            claims.Add(new Claim("tenant_id", tenantId.Value));

        // Copy all roles claims
        foreach (var role in principal.FindAll(ClaimTypes.Role))
            claims.Add(new Claim("roles", role.Value));

        foreach (var role in principal.FindAll("roles"))
            claims.Add(new Claim("roles", role.Value));

        // Emit trace_id claim for OpenTelemetry trace continuity (M3 guard)
        if (_options.EmitTraceIdClaim
            && Activity.Current is { TraceId: var traceId } activity
            && activity.TraceId != default
            && _httpContextAccessor?.HttpContext is not null)
        {
            claims.Add(new Claim("trace_id", traceId.ToHexString()));
        }

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.InternalIssuer,
            Audience = _options.InternalAudience,
            IssuedAt = now,
            NotBefore = now,
            Expires = now.Add(_options.InternalTokenLifetime),
            Subject = new ClaimsIdentity(claims),
            SigningCredentials = _credentials,
        };

        var handler = new JsonWebTokenHandler();
        return handler.CreateToken(descriptor);
    }
}
