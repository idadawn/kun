using System.Security.Claims;
using FluentAssertions;
using Kun.Identity.AspNetCore;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Kun.Identity.Tests.AspNetCore;

public sealed class KunCurrentUserTests
{
    private static readonly string[] AdminEditorRoles = ["admin", "editor"];

    [Fact]
    public void Anonymous_WhenNoHttpContext_ReturnsUnauthenticated()
    {
        var accessor = new HttpContextAccessor { HttpContext = null };
        var user = new KunCurrentUser(accessor);

        user.IsAuthenticated.Should().BeFalse();
        user.UserId.Should().BeNull();
        user.TenantId.Should().BeNull();
        user.Email.Should().BeNull();
        user.Name.Should().BeNull();
        user.Roles.Should().BeEmpty();
        user.Principal.Should().BeNull();
    }

    [Fact]
    public void Authenticated_ProjectsClaimsOntoProperties()
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "user-7"),
            new(KunCurrentUser.TenantIdClaimType, "tenant-z"),
            new(ClaimTypes.Email, "carol@example.com"),
            new(ClaimTypes.Name, "Carol"),
            new(ClaimTypes.Role, "admin"),
            new(ClaimTypes.Role, "editor"),
        };
        var identity = new ClaimsIdentity(claims, authenticationType: "test");
        var ctx = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
        var accessor = new HttpContextAccessor { HttpContext = ctx };
        var user = new KunCurrentUser(accessor);

        user.IsAuthenticated.Should().BeTrue();
        user.UserId.Should().Be("user-7");
        user.TenantId.Should().Be("tenant-z");
        user.Email.Should().Be("carol@example.com");
        user.Name.Should().Be("Carol");
        user.Roles.Should().BeEquivalentTo(AdminEditorRoles);
        user.Principal.Should().NotBeNull();
    }

    [Fact]
    public void SubClaim_FallsBackWhenNameIdentifierAbsent()
    {
        var identity = new ClaimsIdentity(new[] { new Claim("sub", "fallback-id") }, authenticationType: "test");
        var ctx = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
        var user = new KunCurrentUser(new HttpContextAccessor { HttpContext = ctx });

        user.UserId.Should().Be("fallback-id");
    }
}
