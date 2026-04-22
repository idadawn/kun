using System.Security.Claims;
using FluentAssertions;
using Kun.Identity.Configuration;
using Kun.Identity.Implementations;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Kun.Identity.Tests;

public sealed class KunJwtValidatorTests
{
    private const string Issuer = "https://idp.test";
    private const string Audience = "kun-api";

    [Fact]
    public async Task Validate_HappyPath_ReturnsPrincipalWithExpectedClaims()
    {
        var key = TestKeys.CreateRsaKey();
        var token = IssueToken(key, lifetime: TimeSpan.FromMinutes(5), audience: Audience);
        var validator = BuildValidator(key);

        var principal = await validator.ValidateAsync(token, TestContext.Current.CancellationToken);

        principal.Should().NotBeNull();
        principal!.FindFirst("sub")?.Value.Should().Be("user-1");
        principal.FindFirst("tid")?.Value.Should().Be("tenant-a");
    }

    [Fact]
    public async Task Validate_ExpiredToken_ReturnsNull()
    {
        var key = TestKeys.CreateRsaKey();
        var token = IssueToken(key, lifetime: TimeSpan.FromSeconds(-10), audience: Audience);
        var validator = BuildValidator(key);

        var principal = await validator.ValidateAsync(token, TestContext.Current.CancellationToken);

        principal.Should().BeNull();
    }

    [Fact]
    public async Task Validate_WrongAudience_ReturnsNull()
    {
        var key = TestKeys.CreateRsaKey();
        var token = IssueToken(key, lifetime: TimeSpan.FromMinutes(5), audience: "some-other-aud");
        var validator = BuildValidator(key);

        var principal = await validator.ValidateAsync(token, TestContext.Current.CancellationToken);

        principal.Should().BeNull();
    }

    [Fact]
    public async Task Validate_WrongSigningKey_ReturnsNull()
    {
        var actualKey = TestKeys.CreateRsaKey();
        var alienKey = TestKeys.CreateRsaKey();
        var token = IssueToken(alienKey, lifetime: TimeSpan.FromMinutes(5), audience: Audience);
        var validator = BuildValidator(actualKey);

        var principal = await validator.ValidateAsync(token, TestContext.Current.CancellationToken);

        principal.Should().BeNull();
    }

    private static string IssueToken(RsaSecurityKey signingKey, TimeSpan lifetime, string audience)
    {
        var handler = new JsonWebTokenHandler();
        var now = DateTime.UtcNow;
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = audience,
            Subject = new ClaimsIdentity([
                new Claim("sub", "user-1"),
                new Claim("tid", "tenant-a"),
            ]),
            IssuedAt = now.AddSeconds(-30),
            NotBefore = now.AddSeconds(-30),
            Expires = now.Add(lifetime),
            SigningCredentials = new SigningCredentials(signingKey, SecurityAlgorithms.RsaSha256),
        };
        return handler.CreateToken(descriptor);
    }

    private static KunJwtValidator BuildValidator(RsaSecurityKey signingKey)
    {
        var options = new KunJwtBearerOptions
        {
            Issuer = Issuer,
            Audience = Audience,
            ClockSkew = TimeSpan.Zero,
        };
        options.IssuerSigningKeys.Add(signingKey);
        return new KunJwtValidator(
            Options.Create(options),
            NullLogger<KunJwtValidator>.Instance);
    }
}
