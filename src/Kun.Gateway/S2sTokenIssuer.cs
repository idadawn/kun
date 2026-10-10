using System.Globalization;
using System.Security.Claims;
using Kun.Gateway.Configuration;
using Kun.Gateway.Identity;
using Kun.Gateway.Jwks;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Kun.Gateway;

/// <summary>
/// Issues the closed, short-lived S2S claim contract with RS256 and the shared rotating JWKS ring.
/// An authenticated principal/resolver must provide explicit user/service identity, tenant context and user session/revision fields.
/// Trace context travels in headers, not additional JWT payload fields.
/// </summary>
public sealed class S2sTokenIssuer : IDisposable
{
    private readonly S2sSigningKeyRing _keys;
    private readonly bool _ownsKeys;
    private readonly KunGatewayOptions _options;
    private readonly IS2sPrincipalResolver _resolver;

    /// <summary>
    /// Initializes the issuer with an explicit principal resolver. Prefer this ctor —
    /// the 3-arg overload is kept for binary compatibility but is deprecated.
    /// </summary>
    /// <param name="options">Gateway configuration options.</param>
    /// <param name="env">Host environment used to differentiate Development from Production.</param>
    /// <param name="resolver">Principal resolver (use <see cref="NoOpS2sPrincipalResolver.Instance"/> for the identity transform).</param>
    /// <param name="httpContextAccessor">
    /// Retained for constructor compatibility. Trace context is now propagated through HTTP headers.
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
                || !CanonicalClaimTypes.Contains(type)))
        {
            throw new ArgumentException("S2S supports only the fixed canonical claim set; arbitrary forwarded claims are not permitted.", nameof(options));
        }
        _resolver = resolver;

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
    /// principal, then the canonical identity/scope/session fields are validated and projected.
    /// Missing required identity fields fail closed; arbitrary claims are not forwarded.
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
    public Task<string> IssueForAsync(ClaimsPrincipal externalPrincipal, CancellationToken cancellationToken = default)
        => IssueForAudienceAsync(externalPrincipal, _options.InternalAudience, cancellationToken);

    /// <summary>Issues the canonical closed S2S payload for one explicitly selected downstream audience.</summary>
    public async Task<string> IssueForAudienceAsync(ClaimsPrincipal externalPrincipal, string audience, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(externalPrincipal); cancellationToken.ThrowIfCancellationRequested();
        if (externalPrincipal.Identity?.IsAuthenticated != true) throw new InvalidOperationException("S2S issuance requires an authenticated external principal.");
        ValidateText(audience, "aud", 255); ValidateText(_options.InternalIssuer, "iss", 255);
        var lifetime = _options.InternalTokenLifetime;
        if (lifetime < TimeSpan.FromSeconds(1) || lifetime > TimeSpan.FromMinutes(5))
            throw new InvalidOperationException("Internal S2S token lifetime must be between one second and five minutes.");
        var principal = await _resolver.ResolveAsync(externalPrincipal, cancellationToken).ConfigureAwait(false);
        if (principal?.Identity?.IsAuthenticated != true) throw new InvalidOperationException("S2S resolver did not return an authenticated principal.");
        cancellationToken.ThrowIfCancellationRequested();
        var sub = Scalar(principal, "sub", required: false) ?? Scalar(principal, ClaimTypes.NameIdentifier, required: true)!;
        ValidateText(sub, "sub", 255);
        var kind = Scalar(principal, "principal_kind", required: true)!;
        if (kind is not "user" and not "service" || principal.HasClaim(claim => claim.Type == "act"))
            throw new InvalidOperationException("S1 S2S supports only user/service principals without delegation.");
        var tenantClaims = principal.FindAll("tenant_id").ToArray();
        if (tenantClaims.Length != 1) throw new InvalidOperationException("Exactly one explicit tenant_id (UUID or JSON null) is required.");
        var tenant = tenantClaims[0].ValueType == JsonClaimValueTypes.JsonNull ? null : Uuid(tenantClaims[0].Value, "tenant_id");
        var roles = principal.Claims.Where(claim => claim.Type == ClaimTypes.Role || claim.Type is "role" or "roles")
            .Select(claim => claim.Value).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.Ordinal).ToArray();
        if (roles.Length > 32) throw new InvalidOperationException("S2S roles exceed the contract bound.");
        foreach (var role in roles) ValidateText(role, "roles", 64);
        var scope = string.Join(" ", principal.FindAll("scope").SelectMany(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Distinct(StringComparer.Ordinal));
        if (scope.Length > 4096 || scope.Any(character => !IsScopeCharacter(character)))
            throw new InvalidOperationException("S2S scope is not a valid bounded OAuth scope string.");
        var jti = Guid.NewGuid().ToString("D");
        var claims = new Dictionary<string, object>
        {
            ["sub"] = sub,
            ["principal_kind"] = kind,
            ["tenant_id"] = tenant!, // Intentional JSON null for an explicit platform principal.
            ["aud"] = new[] { audience },
            ["roles"] = roles,
            ["scope"] = scope,
            ["jti"] = jti,
            ["session_id"] = kind == "service" ? jti : Uuid(Scalar(principal, "session_id", required: true)!, "session_id")
        };
        var membership = Scalar(principal, "membership_id", required: kind == "user" && tenant is not null);
        if (kind == "service" && membership is not null) throw new InvalidOperationException("Service principals cannot carry a membership_id.");
        if (membership is not null) claims["membership_id"] = Uuid(membership, "membership_id");
        var revision = Scalar(principal, "identity_revision", required: kind == "user");
        if (revision is not null)
        {
            if (!long.TryParse(revision, NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value < 0)
                throw new InvalidOperationException("identity_revision must be a nonnegative integer.");
            claims["identity_revision"] = value;
        }
        var project = Scalar(principal, "project_id", required: false);
        if (project is not null) claims["project_id"] = Uuid(project, "project_id");
        var plugin = Scalar(principal, "plugin_id", required: false);
        if (plugin is not null)
        {
            var segments = plugin.Split('.');
            if (segments.Length < 2 || segments.Any(segment => segment.Length == 0 || segment[0] is < 'a' or > 'z' || segment.Any(character => !char.IsAsciiLetterLower(character) && !char.IsAsciiDigit(character) && character != '-')))
                throw new InvalidOperationException("plugin_id must be a dotted component identifier.");
            claims["plugin_id"] = plugin;
        }
        var now = DateTime.UtcNow;
        return _keys.CreateToken(new SecurityTokenDescriptor { Issuer = _options.InternalIssuer, IssuedAt = now, NotBefore = now, Expires = now + lifetime, Claims = claims });
    }

    private static string? Scalar(ClaimsPrincipal principal, string type, bool required)
    {
        var claims = principal.FindAll(type).ToArray();
        if (claims.Length > 1 || required && claims.Length == 0 || claims.Length == 1 && string.IsNullOrWhiteSpace(claims[0].Value))
            throw new InvalidOperationException($"S2S requires one valid {type} claim.");
        return claims.Length == 0 ? null : claims[0].Value;
    }
    private static bool IsScopeCharacter(char value) => value == ' ' || value == '!' || value >= '#' && value <= '[' || value >= ']' && value <= '~';
    private static string Uuid(string value, string name)
    {
        if (!Guid.TryParse(value, out var id)) throw new InvalidOperationException($"S2S {name} must be a UUID.");
        return id.ToString("D");
    }
    private static void ValidateText(string value, string name, int maximum)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximum || value.Any(char.IsControl)) throw new InvalidOperationException($"S2S {name} is invalid.");
    }
    private static readonly HashSet<string> CanonicalClaimTypes = new(StringComparer.Ordinal)
    { "principal_kind", "scope", "session_id", "membership_id", "identity_revision", "project_id", "plugin_id" };
}
