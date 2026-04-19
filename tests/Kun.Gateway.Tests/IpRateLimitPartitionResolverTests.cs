using System.Net;
using FluentAssertions;
using Kun.Gateway.RateLimiting.Resolvers;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Kun.Gateway.Tests;

/// <summary>
/// Unit tests for <see cref="IpRateLimitPartitionResolver"/>.
/// </summary>
public sealed class IpRateLimitPartitionResolverTests
{
    private readonly IpRateLimitPartitionResolver _resolver = new();

    [Fact]
    public void ResolvePartitionKey_UsesRemoteIpAddress()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("192.168.1.42");

        // Act
        var key = _resolver.ResolvePartitionKey(context);

        // Assert
        key.Should().Be("192.168.1.42");
    }

    [Fact]
    public void ResolvePartitionKey_PrefersXForwardedFor_WhenPresent()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.1"); // proxy IP
        context.Request.Headers["X-Forwarded-For"] = "203.0.113.5, 10.0.0.1";

        // Act
        var key = _resolver.ResolvePartitionKey(context);

        // Assert — first IP in X-Forwarded-For is the originating client
        key.Should().Be("203.0.113.5");
    }

    [Fact]
    public void ResolvePartitionKey_NoIp_ReturnsUnknown()
    {
        // Arrange
        var context = new DefaultHttpContext();
        // RemoteIpAddress is null by default in DefaultHttpContext

        // Act
        var key = _resolver.ResolvePartitionKey(context);

        // Assert
        key.Should().Be("unknown");
    }
}
