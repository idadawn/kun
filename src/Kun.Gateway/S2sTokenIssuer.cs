using System.Diagnostics;
using System.Security.Claims;
using Kun.Gateway.Configuration;
using Kun.Gateway.Identity;
using Kun.Gateway.Jwks;
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
public sealed class S2sTokenIssuer : IDisposable
{
    private readonly S2sSigningKeyRing _keys;
    private readonly bool _ownsKeys;
    private readonly KunGatewayOptions _options;
    private readonly IS2sPrincipalResolver _resolver;
    private readonly IHttpContextAccessor? _httpContextAccessor;
    private readonly HashSet<string> _forwardedClaimTypes;

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
        : this(options, env, resolver, httpContextAccessor, null)
    {
    }

    internal S2sTokenIssuer(IOptions<KunGatewayOptions> options, IHostEnvironment env, IS2sPrincipalResolver resolver, IHttpContextAccessor? httpContextAccessor, S2sSigningKeyRing? keys)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(resolver);
        _options = options.Value;
        ArgumentNullException.ThrowIfNull(_options.ForwardedClaimTypes);
        if (_options.ForwardedClaimTypes.Length > 32
            || _options.ForwardedClaimTypes.Any(type => string.IsNullOrWhiteSpace(type)
                || type.Length > 128 || type.Any(char.IsControl) || type != type.Trim()
                || IsReservedClaim(type)))
        {
            throw new ArgumentException("Forwarded claim types must be a bounded allowlist of non-reserved claims.", nameof(options));
        }
        _forwardedClaimTypes = _options.ForwardedClaimTypes.ToHashSet(StringComparer.Ordinal);
        _resolver = resolver;
        _httpContextAccessor = httpContextAccessor;

        _ownsKeys = keys is null;
        _keys = keys ?? new S2sSigningKeyRing(options, env, TimeProvider.System, Microsoft.Extensions.Logging.Abstractions.NullLogger<S2sSigningKeyRing>.Instance);
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

    /// <summary>释放通过兼容构造器独立创建的签名状态；DI 共享状态由容器释放。</summary>
    public void Dispose()
    {
        if (_ownsKeys) _keys.Dispose();
    }

    /// <summary>
    /// Issues an internal S2S JWT for the given external <paramref name="externalPrincipal"/>.
    /// The configured <see cref="IS2sPrincipalResolver"/> runs first to produce the effective
    /// principal, then <c>sub</c>, <c>tenant_id</c>, and all <c>roles</c> claims are copied
    /// into the new token.
    /// </summary>
    /// <remarks>
    /// Synchronous convenience wrapper over <see cref="IssueForAsync(ClaimsPrincipal, CancellationToken)"/>.
    /// Prefer the async form on the Gateway request path — it avoids the
    /// <c>GetAwaiter().GetResult()</c> hop and lets resolvers that perform real I/O work
    /// without starving the thread pool.
    /// </remarks>
    /// <param name="externalPrincipal">The authenticated principal from the external IdP JWT.</param>
    /// <returns>A signed RS256 JWT string.</returns>
    public string IssueFor(ClaimsPrincipal externalPrincipal)
    {
        return IssueForAsync(externalPrincipal, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
    }

    /// <summary>
    /// Async variant of <see cref="IssueFor(ClaimsPrincipal)"/>. Awaits the configured
    /// <see cref="IS2sPrincipalResolver"/> without blocking, then mints the token.
    /// </summary>
    /// <param name="externalPrincipal">The authenticated principal from the external IdP JWT.</param>
    /// <param name="cancellationToken">Cancellation token passed to the resolver.</param>
    /// <returns>A signed RS256 JWT string.</returns>
    public async Task<string> IssueForAsync(
        ClaimsPrincipal externalPrincipal,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(externalPrincipal);
        cancellationToken.ThrowIfCancellationRequested();
        var lifetime = _options.InternalTokenLifetime;
        if (lifetime <= TimeSpan.Zero || lifetime > TimeSpan.FromMinutes(5))
        {
            throw new InvalidOperationException("Internal S2S token lifetime must be greater than zero and at most five minutes.");
        }

        var principal = await _resolver
            .ResolveAsync(externalPrincipal, cancellationToken)
            .ConfigureAwait(false);

        ArgumentNullException.ThrowIfNull(principal);
        cancellationToken.ThrowIfCancellationRequested();

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

        // Normalize supported role spellings without duplicating the same grant.
        foreach (var role in principal.Claims
                     .Where(claim => claim.Type == ClaimTypes.Role || claim.Type is "role" or "roles")
                     .Select(claim => claim.Value).Distinct(StringComparer.Ordinal))
            claims.Add(new Claim("roles", role));

        var forwarded = principal.Claims.Where(claim => _forwardedClaimTypes.Contains(claim.Type)).ToArray();
        if (forwarded.Length > 128 || forwarded.Any(claim => claim.Value.Length > 4096))
            throw new InvalidOperationException("Forwarded claims exceed the configured token bounds.");
        foreach (var claim in forwarded)
            claims.Add(new Claim(claim.Type, claim.Value, claim.ValueType));
        claims.Add(new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")));

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
            Expires = now.Add(lifetime),
            Subject = new ClaimsIdentity(claims),
        };

        return _keys.CreateToken(descriptor);
    }

    private static readonly HashSet<string> ReservedClaimTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "iss", "aud", "sub", "tenant_id", "tenant", "roles", "role", "exp", "iat", "nbf", "jti",
        "trace_id", "original_token_jti", ClaimTypes.NameIdentifier, ClaimTypes.Role
    };

    private static bool IsReservedClaim(string type) => ReservedClaimTypes.Contains(type);
}
