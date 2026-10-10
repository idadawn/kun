using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Kun.Gateway.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Kun.Gateway.Tests;

/// <summary>
/// Binary-compat guard for A5: proves the deprecated 3-arg <see cref="S2sTokenIssuer"/>
/// constructor still compiles and issues a functioning token. Consumers that haven't
/// migrated to the 4-arg ctor must not break until 0.2.0.
/// </summary>
public sealed class S2sTokenIssuerObsoleteCtorTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _signingKeyPath;
    private readonly string _publicKeyPath;

    public S2sTokenIssuerObsoleteCtorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"kun-obsolete-{Guid.NewGuid():N}");
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
        }
    }

    [Fact]
    public void Obsolete_Three_Arg_Ctor_Still_Issues_Valid_Token()
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

#pragma warning disable KUN0002 // intentional: this test is the obsolete-ctor compat guard
        var issuer = new S2sTokenIssuer(options, env, httpContextAccessor: null);
#pragma warning restore KUN0002

        var identity = new ClaimsIdentity(
            [new Claim("sub", "compat-user"), new Claim("tenant_id", "00000000-0000-4000-8000-000000000044")],
            authenticationType: "test");
        var principal = S2sPrincipalFixture.Complete(new ClaimsPrincipal(identity));

        var jwt = issuer.IssueFor(principal);

        var parsed = new JwtSecurityTokenHandler().ReadJwtToken(jwt);
        Assert.Equal("compat-user", parsed.Claims.First(c => c.Type == "sub").Value);
        Assert.Equal("00000000-0000-4000-8000-000000000044", parsed.Claims.First(c => c.Type == "tenant_id").Value);
    }
}
