using System.Security.Claims;
using FluentAssertions;
using Kun.Security.Authentication;
using Kun.Security.Security;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Kun.Security.Tests;

public sealed class PrincipalClaimsTests
{
    [Theory]
    [InlineData("user")]
    [InlineData("service")]
    [InlineData("agent")]
    public void TryRead_AuthenticatedKind_PreservesSubjectTuple(string kind)
    {
        var principal = Principal(new Claim("principal_kind", kind));
        PrincipalClaims.TryRead(principal, out var id, out var tenant, out var actual).Should().BeTrue();
        id.Should().Be("same-id"); tenant.Should().Be("tenant-a"); actual.Should().Be(kind);
        var user = new JwtCurrentUser(new Accessor(new DefaultHttpContext { User = principal }));
        user.PrincipalKind.Should().Be(kind); user.UserId.Should().Be("same-id");
    }

    [Fact]
    public void TryRead_LegacyUserAndPlatformNull_AreExplicitlyPreserved()
    {
        PrincipalClaims.TryRead(Principal(), out _, out _, out var kind).Should().BeTrue(); kind.Should().Be("user");
        var platform = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "same-id"), new Claim("tenant_id", "", "JSON_NULL"), new Claim("principal_kind", "service")], "validated"));
        PrincipalClaims.TryRead(platform, out _, out var tenant, out kind).Should().BeTrue(); tenant.Should().BeNull(); kind.Should().Be("service");
    }

    [Fact]
    public void TryRead_AmbiguousOrInvalidClaims_DoesNotDefaultToUser()
    {
        ClaimsPrincipal[] cases = [Principal(new Claim("principal_kind", "")), Principal(new Claim("principal_kind", "other")),
            Principal(new Claim("principal_kind", "service"), new Claim("principal_kind", "user")),
            Principal(new Claim("principal_kind", "service"), new Claim("principal_kind", "service")),
            Principal(new Claim("tenant_id", "tenant-b")), Principal(new Claim(ClaimTypes.NameIdentifier, "different-id")),
            new(new ClaimsIdentity([new Claim("sub", "same-id"), new Claim("principal_kind", "service")]))];
        foreach (var principal in cases)
        {
            PrincipalClaims.TryRead(principal, out var subject, out var tenant, out var kind).Should().BeFalse();
            subject.Should().BeNull(); tenant.Should().BeNull(); kind.Should().BeNull();
            new JwtCurrentUser(new Accessor(new DefaultHttpContext { User = principal })).PrincipalKind.Should().BeEmpty();
        }
    }

    [Fact]
    public void TryRead_SecondaryIdentityCannotSupplySubjectKindOrRoles()
    {
        var primary = new ClaimsIdentity([new Claim("sub", "same-id"), new Claim("principal_kind", "service"), new Claim("tenant_id", "tenant-a")], "validated");
        var principal = new ClaimsPrincipal([primary, new ClaimsIdentity([new Claim("principal_kind", "user"), new Claim("roles", "admin"), new Claim("tenant_id", "tenant-b")])]);
        var current = new JwtCurrentUser(new Accessor(new DefaultHttpContext { User = principal }));
        current.PrincipalKind.Should().Be("service"); current.Roles.Should().BeEmpty();
        var missingSubject = new ClaimsPrincipal([new ClaimsIdentity([new Claim("principal_kind", "service")], "validated"),
            new ClaimsIdentity([new Claim("sub", "same-id")])]);
        PrincipalClaims.TryRead(missingSubject, out _, out _, out _).Should().BeFalse();
    }

    private static ClaimsPrincipal Principal(params Claim[] extra) => new(new ClaimsIdentity(new[] { new Claim("sub", "same-id"), new Claim("tenant_id", "tenant-a") }.Concat(extra), "validated"));
    private sealed class Accessor(HttpContext? context) : IHttpContextAccessor { public HttpContext? HttpContext { get; set; } = context; }
}
