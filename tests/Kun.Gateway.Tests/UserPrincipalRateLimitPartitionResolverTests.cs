using System.Security.Claims;
using Kun.Gateway.RateLimiting.Resolvers;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Kun.Gateway.Tests;

public sealed class UserPrincipalRateLimitPartitionResolverTests
{
    [Fact]
    public void ResolvePartitionKey_WithSubClaim_ReturnsSubValue()
    {
        var resolver = new UserPrincipalRateLimitPartitionResolver();
        var ctx = BuildContext(new Claim("sub", "user-42"));

        Assert.Equal("user-42", resolver.ResolvePartitionKey(ctx));
    }

    [Fact]
    public void ResolvePartitionKey_WithoutSubClaim_ReturnsAnonymous()
    {
        var resolver = new UserPrincipalRateLimitPartitionResolver();
        var ctx = BuildContext(/* no claims */);

        Assert.Equal("anonymous", resolver.ResolvePartitionKey(ctx));
    }

    [Fact]
    public void ResolvePartitionKey_WithEmptySubClaim_ReturnsAnonymous()
    {
        var resolver = new UserPrincipalRateLimitPartitionResolver();
        var ctx = BuildContext(new Claim("sub", string.Empty));

        Assert.Equal("anonymous", resolver.ResolvePartitionKey(ctx));
    }

    [Fact]
    public void ResolvePartitionKey_WithWhitespaceSubClaim_ReturnsAnonymous()
    {
        var resolver = new UserPrincipalRateLimitPartitionResolver();
        var ctx = BuildContext(new Claim("sub", "   "));

        Assert.Equal("anonymous", resolver.ResolvePartitionKey(ctx));
    }

    [Fact]
    public void ResolvePartitionKey_NullContext_Throws()
    {
        var resolver = new UserPrincipalRateLimitPartitionResolver();
        Assert.Throws<ArgumentNullException>(() => resolver.ResolvePartitionKey(null!));
    }

    [Fact]
    public void ResolvePartitionKey_DifferentUsers_ProduceDifferentKeys()
    {
        var resolver = new UserPrincipalRateLimitPartitionResolver();
        var ctxA = BuildContext(new Claim("sub", "user-a"));
        var ctxB = BuildContext(new Claim("sub", "user-b"));

        Assert.NotEqual(resolver.ResolvePartitionKey(ctxA), resolver.ResolvePartitionKey(ctxB));
    }

    private static DefaultHttpContext BuildContext(params Claim[] claims)
    {
        var ctx = new DefaultHttpContext();
        var identity = new ClaimsIdentity(claims, authenticationType: claims.Length > 0 ? "test" : null);
        ctx.User = new ClaimsPrincipal(identity);
        return ctx;
    }
}
