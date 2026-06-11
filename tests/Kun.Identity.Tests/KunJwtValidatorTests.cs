using System.Security.Claims;
using FluentAssertions;
using Kun.Identity.Configuration;
using Kun.Identity.Implementations;
using Microsoft.Extensions.Logging;
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

    [Fact]
    public void Ctor_ThrowsWhenIssuerIsNull()
    {
        var options = new KunJwtBearerOptions
        {
            Issuer = null,
            Audience = Audience,
        };
        options.IssuerSigningKeys.Add(TestKeys.CreateRsaKey());

        var act = () => new KunJwtValidator(Options.Create(options), NullLogger<KunJwtValidator>.Instance);

        act.Should().Throw<InvalidOperationException>().WithMessage("*Issuer*");
    }

    [Fact]
    public void Ctor_ThrowsWhenAudienceIsNull()
    {
        var options = new KunJwtBearerOptions
        {
            Issuer = Issuer,
            Audience = null,
        };
        options.IssuerSigningKeys.Add(TestKeys.CreateRsaKey());

        var act = () => new KunJwtValidator(Options.Create(options), NullLogger<KunJwtValidator>.Instance);

        act.Should().Throw<InvalidOperationException>().WithMessage("*Audience*");
    }

    [Fact]
    public void Ctor_ThrowsWhenIssuerIsWhitespace()
    {
        var options = new KunJwtBearerOptions
        {
            Issuer = "   ",
            Audience = Audience,
        };
        options.IssuerSigningKeys.Add(TestKeys.CreateRsaKey());

        var act = () => new KunJwtValidator(Options.Create(options), NullLogger<KunJwtValidator>.Instance);

        act.Should().Throw<InvalidOperationException>().WithMessage("*Issuer*");
    }

    [Fact]
    public void Ctor_ThrowsWhenValidateSignatureTrueButNoKeysAndNoJwksUri()
    {
#pragma warning disable CS0618
        var options = new KunJwtBearerOptions
        {
            Issuer = Issuer,
            Audience = Audience,
            ValidateSignature = true,
        };
#pragma warning restore CS0618

        var act = () => new KunJwtValidator(Options.Create(options), NullLogger<KunJwtValidator>.Instance);

        act.Should().Throw<InvalidOperationException>().WithMessage("*ValidateSignature*");
    }

    [Fact]
    public void Ctor_LogsCriticalWhenSignatureValidationDisabled()
    {
        var logger = new TestLogger<KunJwtValidator>();
        var options = new KunJwtBearerOptions
        {
            Issuer = Issuer,
            Audience = Audience,
        };
#pragma warning disable CS0618
        options.ValidateSignature = false;
#pragma warning restore CS0618

        var validator = new KunJwtValidator(Options.Create(options), logger);

        logger.LatestLevel.Should().Be(LogLevel.Critical);
        logger.LatestMessage.Should().Contain("signature validation is DISABLED");
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

    private sealed class TestLogger<T> : ILogger<T>
    {
        public LogLevel LatestLevel { get; private set; }
        public string? LatestMessage { get; private set; }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            LatestLevel = logLevel;
            LatestMessage = formatter(state, exception);
        }
    }
}
