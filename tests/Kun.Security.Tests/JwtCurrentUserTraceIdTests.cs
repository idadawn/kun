using System.Security.Claims;
using FluentAssertions;
using Kun.Core.Security;
using Kun.Security.Security;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Kun.Security.Tests;

/// <summary>
/// Covers <see cref="JwtCurrentUser.TraceId"/> and its conformance to
/// <see cref="ITracedCurrentUser"/> introduced in P0-2 (commit 4564278).
/// </summary>
public sealed class JwtCurrentUserTraceIdTests
{
    [Fact]
    public void TraceId_WhenClaimPresent_ReturnsClaimValue()
    {
        var user = BuildUser(("trace_id", "00112233445566778899aabbccddeeff"));
        user.TraceId.Should().Be("00112233445566778899aabbccddeeff");
    }

    [Fact]
    public void TraceId_WhenClaimAbsent_ReturnsNull()
    {
        var user = BuildUser(("sub", "alice"));
        user.TraceId.Should().BeNull();
    }

    [Fact]
    public void TraceId_WhenNoHttpContext_ReturnsNull()
    {
        var accessor = new StubAccessor(null);
        var user = new JwtCurrentUser(accessor);
        user.TraceId.Should().BeNull();
    }

    [Fact]
    public void JwtCurrentUser_IsCastable_ToITracedCurrentUser()
    {
        var user = BuildUser(("trace_id", "abc"));

        user.Should().BeAssignableTo<ITracedCurrentUser>();
        (user as ITracedCurrentUser)!.TraceId.Should().Be("abc");
    }

    private static JwtCurrentUser BuildUser(params (string type, string value)[] claims)
    {
        var identity = new ClaimsIdentity(
            claims.Select(c => new Claim(c.type, c.value)).ToArray(),
            authenticationType: "test");
        var principal = new ClaimsPrincipal(identity);
        var ctx = new DefaultHttpContext { User = principal };
        return new JwtCurrentUser(new StubAccessor(ctx));
    }

    private sealed class StubAccessor : IHttpContextAccessor
    {
        public StubAccessor(HttpContext? ctx) => HttpContext = ctx;
        public HttpContext? HttpContext { get; set; }
    }
}
