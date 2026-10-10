#pragma warning disable KUNS2S001 // Explicit compatibility tests for retired issuer APIs; new consumers must fail compilation.
using System.Security.Claims;
using FluentAssertions;
using Kun.Identity.Configuration;
using Kun.Identity.Implementations;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Kun.Identity.Tests;

public sealed class KunJwtIssuerTests
{
    [Fact]
    public async Task Issue_ThenValidate_RoundTrips()
    {
        var key = TestKeys.CreateRsaKey("test-kid");

        var issuer = new KunJwtIssuer(Options.Create(new KunJwtIssuerOptions
        {
            Issuer = "kun.internal",
            Audience = "kun-internal",
            SigningKey = key,
            DefaultLifetime = TimeSpan.FromSeconds(60),
            KeyId = "test-kid",
        }));

        var token = issuer.Issue(
            [new Claim("sub", "svc-gateway"), new Claim("tid", "tenant-a")],
            lifetime: TimeSpan.FromMinutes(2));

        var validatorOptions = new KunJwtBearerOptions
        {
            Issuer = "kun.internal",
            Audience = "kun-internal",
            ClockSkew = TimeSpan.Zero,
        };
        validatorOptions.IssuerSigningKeys.Add(key);

        var validator = new KunJwtValidator(
            Options.Create(validatorOptions),
            NullLogger<KunJwtValidator>.Instance);

        var principal = await validator.ValidateAsync(token, TestContext.Current.CancellationToken);

        principal.Should().NotBeNull();
        principal!.FindFirst("sub")?.Value.Should().Be("svc-gateway");
        principal.FindFirst("tid")?.Value.Should().Be("tenant-a");
    }

    [Fact]
    public void MissingSigningKey_Throws()
    {
        var options = Options.Create(new KunJwtIssuerOptions
        {
            Issuer = "kun.internal",
            Audience = "kun-internal",
        });

        var act = () => new KunJwtIssuer(options);
        act.Should().Throw<InvalidOperationException>().WithMessage("*SigningKey*SigningKeyPem*");
    }
}
