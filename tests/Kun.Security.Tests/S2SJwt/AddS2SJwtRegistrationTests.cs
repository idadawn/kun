#pragma warning disable KUNS2S001 // Explicit compatibility tests for retired issuer APIs; new consumers must fail compilation.
using System.Security.Cryptography;
using FluentAssertions;
using Kun.Security.S2SJwt;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Kun.Security.Tests.S2SJwt;

public sealed class AddS2SJwtRegistrationTests : IDisposable
{
    private readonly ECDsa _ecdsa;
    private readonly string _privateKeyPem;

    public AddS2SJwtRegistrationTests()
    {
        _ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        _privateKeyPem = _ecdsa.ExportECPrivateKeyPem();
    }

    [Fact]
    public async Task AddS2SJwt_LambdaConfiguration_ResolvesWorkingIssuerAndVerifier()
    {
        // Locks the registration pattern documented in docs/security/s2s-jwt.md:
        // property assignments inside the configure delegate must compile (kun#11
        // tracked CS8852 here while the options were init-only).
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<ILogger<S2SJwtIssuer>>());
        services.AddSingleton(Substitute.For<ILogger<S2SJwtVerifier>>());
        services.AddS2SJwt(options =>
        {
            options.SigningKeyPem = _privateKeyPem;
            options.KeyId = "k1";
            options.Issuer = "kun-gateway";
        });

        await using var provider = services.BuildServiceProvider();
        var issuer = provider.GetRequiredService<IS2SJwtIssuer>();
        var verifier = provider.GetRequiredService<IS2SJwtVerifier>();

        var token = await issuer.IssueAsync(
            new S2SJwtIssueRequest(
                UserId: "user-001",
                TenantId: "tenant-001",
                Audience: ["neuroagent-platform"],
                Scopes: ["threads:read"],
                SessionId: "session-001"),
            TestContext.Current.CancellationToken);

        var principal = await verifier.VerifyAsync(
            token, "neuroagent-platform", TestContext.Current.CancellationToken);

        principal.Should().NotBeNull();
        principal!.FindFirst("sub")!.Value.Should().Be("user-001");
    }

    public void Dispose() => _ecdsa.Dispose();
}
