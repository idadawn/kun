using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Kun.Gateway.Configuration;
using Kun.Gateway.Identity;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Kun.Gateway.Tests;

/// <summary>
/// Verifies that <see cref="IS2sPrincipalResolver"/> runs before claims are mapped into the
/// minted S2S token, so a custom resolver can enrich (add / replace) claims that the external
/// IdP does not own.
/// </summary>
public sealed class PrincipalResolverTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _signingKeyPath;
    private readonly string _publicKeyPath;

    public PrincipalResolverTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"kun-resolver-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _signingKeyPath = Path.Combine(_tempDir, "signing.pem");
        _publicKeyPath = Path.Combine(_tempDir, "public.pem");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch (IOException)
        {
            // best effort during test teardown
        }
    }

    [Fact]
    public void Default_Resolver_Is_NoOp()
    {
        var issuer = CreateIssuer(NoOpS2sPrincipalResolver.Instance);

        var external = CreatePrincipal(("sub", "alice"), ("tenant_id", "t-42"));
        var jwt = issuer.IssueFor(external);

        var handler = new JwtSecurityTokenHandler();
        var token = handler.ReadJwtToken(jwt);
        Assert.Equal("alice", token.Claims.First(c => c.Type == JwtRegisteredClaimNames.Sub).Value);
        Assert.Equal("t-42", token.Claims.First(c => c.Type == "tenant_id").Value);
    }

    [Fact]
    public void Custom_Resolver_Runs_Before_Claim_Mapping()
    {
        // Resolver injects a synthetic tenant_id the external principal did not have, then
        // issuer copies it into the minted token.
        var resolver = new EnrichingResolver(addTenantId: "tenant-from-lookup");
        var issuer = CreateIssuer(resolver);

        var external = CreatePrincipal(("sub", "bob"));
        var jwt = issuer.IssueFor(external);

        var handler = new JwtSecurityTokenHandler();
        var token = handler.ReadJwtToken(jwt);
        Assert.Equal("bob", token.Claims.First(c => c.Type == JwtRegisteredClaimNames.Sub).Value);
        Assert.Equal("tenant-from-lookup", token.Claims.First(c => c.Type == "tenant_id").Value);
    }

    [Fact]
    public void Custom_Resolver_Can_Overwrite_Roles()
    {
        var resolver = new RoleReplacingResolver(replaceWith: ["admin", "auditor"]);
        var issuer = CreateIssuer(resolver);

        var external = CreatePrincipal(("sub", "carol"));
        external.Identities.First().AddClaim(new Claim(ClaimTypes.Role, "viewer"));

        var jwt = issuer.IssueFor(external);
        var token = new JwtSecurityTokenHandler().ReadJwtToken(jwt);
        var roles = token.Claims.Where(c => c.Type == "roles").Select(c => c.Value).ToHashSet();

        Assert.Contains("admin", roles);
        Assert.Contains("auditor", roles);
        Assert.DoesNotContain("viewer", roles);
    }

    [Fact]
    public void Resolver_Exception_Propagates_Instead_Of_Issuing_Empty_Token()
    {
        var resolver = new ThrowingResolver();
        var issuer = CreateIssuer(resolver);

        var external = CreatePrincipal(("sub", "dave"));
        Assert.Throws<InvalidOperationException>(() => issuer.IssueFor(external));
    }

    private S2sTokenIssuer CreateIssuer(IS2sPrincipalResolver resolver)
    {
        var options = Options.Create(new KunGatewayOptions
        {
            ExternalIssuer = "https://idp.example",
            ExternalAudience = "kun-external",
            ExternalJwksUri = "https://idp.example/.well-known/jwks.json",
            SigningKeyPath = _signingKeyPath,
            PublicKeyPath = _publicKeyPath,
        });

        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName.Returns("Development");

        return new S2sTokenIssuer(options, env, resolver);
    }

    private static ClaimsPrincipal CreatePrincipal(params (string Type, string Value)[] claims)
    {
        var identity = new ClaimsIdentity(claims.Select(c => new Claim(c.Type, c.Value)), "test");
        return new ClaimsPrincipal(identity);
    }

    private sealed class EnrichingResolver : IS2sPrincipalResolver
    {
        private readonly string _addTenantId;
        public EnrichingResolver(string addTenantId) => _addTenantId = addTenantId;

        public Task<ClaimsPrincipal> ResolveAsync(ClaimsPrincipal externalPrincipal, CancellationToken cancellationToken)
        {
            var claims = externalPrincipal.Claims
                .Where(c => c.Type != "tenant_id")
                .Append(new Claim("tenant_id", _addTenantId));
            var identity = new ClaimsIdentity(claims, "enriched");
            return Task.FromResult(new ClaimsPrincipal(identity));
        }
    }

    private sealed class RoleReplacingResolver : IS2sPrincipalResolver
    {
        private readonly IReadOnlyList<string> _replaceWith;
        public RoleReplacingResolver(IReadOnlyList<string> replaceWith) => _replaceWith = replaceWith;

        public Task<ClaimsPrincipal> ResolveAsync(ClaimsPrincipal externalPrincipal, CancellationToken cancellationToken)
        {
            var nonRoleClaims = externalPrincipal.Claims.Where(c => c.Type != ClaimTypes.Role && c.Type != "roles");
            var newRoles = _replaceWith.Select(r => new Claim("roles", r));
            var identity = new ClaimsIdentity(nonRoleClaims.Concat(newRoles), "role-replaced");
            return Task.FromResult(new ClaimsPrincipal(identity));
        }
    }

    private sealed class ThrowingResolver : IS2sPrincipalResolver
    {
        public Task<ClaimsPrincipal> ResolveAsync(ClaimsPrincipal externalPrincipal, CancellationToken cancellationToken)
            => Task.FromException<ClaimsPrincipal>(new InvalidOperationException("tenant lookup failed"));
    }
}
