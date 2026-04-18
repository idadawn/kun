using System.Security.Claims;
using FluentAssertions;
using Kun.Gateway.RateLimiting.Resolvers;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Kun.Gateway.Tests;

/// <summary>
/// Unit tests for <see cref="TenantRateLimitPartitionResolver"/>.
/// </summary>
public sealed class TenantRateLimitPartitionResolverTests
{
    private readonly TenantRateLimitPartitionResolver _resolver = new();

    [Fact]
    public void ResolvePartitionKey_WithTenantIdClaim_ReturnsTenantId()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.User = new ClaimsPrincipal(
            new ClaimsIdentity([new Claim("tenant_id", "tenant-abc")], "test"));

        // Act
        var key = _resolver.ResolvePartitionKey(context);

        // Assert
        key.Should().Be("tenant-abc");
    }

    [Fact]
    public void ResolvePartitionKey_WithoutTenantIdClaim_ReturnsAnonymous()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity()); // no claims

        // Act
        var key = _resolver.ResolvePartitionKey(context);

        // Assert
        key.Should().Be("anonymous");
    }

    [Fact]
    public void ResolvePartitionKey_UnauthenticatedUser_ReturnsAnonymous()
    {
        // Arrange
        var context = new DefaultHttpContext(); // User.Identity is null by default

        // Act
        var key = _resolver.ResolvePartitionKey(context);

        // Assert
        key.Should().Be("anonymous");
    }
}
