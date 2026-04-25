using FluentAssertions;
using Kun.Sandbox.Abstractions;
using Kun.Sandbox.Implementations;
using Xunit;

namespace Kun.Sandbox.Tests;

public sealed class E2BCloudSandboxProviderTests
{
    [Fact]
    public void Constructor_stores_credentials_for_future_Mode_D()
    {
        var provider = new E2BCloudSandboxProvider("api-key-123", "team-abc");

        provider.ApiKey.Should().Be("api-key-123");
        provider.TeamId.Should().Be("team-abc");
    }

    [Fact]
    public async Task CreateAsync_throws_NotImplementedException_with_Mode_D_reference()
    {
        var ct = TestContext.Current.CancellationToken;
        var provider = new E2BCloudSandboxProvider("api-key-123", "team-abc");

        var act = () => provider.CreateAsync(new SandboxCreateOptions(), ct);

        (await act.Should().ThrowAsync<NotImplementedException>())
            .WithMessage("*Phase 2+*Mode D*");
    }

    [Fact]
    public void Constructor_rejects_empty_api_key_or_team()
    {
        Action emptyKey = () => { _ = new E2BCloudSandboxProvider("", "team"); };
        Action emptyTeam = () => { _ = new E2BCloudSandboxProvider("key", ""); };

        emptyKey.Should().Throw<ArgumentException>();
        emptyTeam.Should().Throw<ArgumentException>();
    }
}
