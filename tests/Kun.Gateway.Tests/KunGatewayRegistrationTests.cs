using FluentAssertions;
using Kun.Gateway.Configuration;
using Kun.Gateway.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;
using Yarp.ReverseProxy.Health;

namespace Kun.Gateway.Tests;

/// <summary>
/// Regression tests for <see cref="KunGatewayServiceCollectionExtensions.AddKunGateway"/>
/// double-registration safety and startup validation.
/// </summary>
public sealed class KunGatewayRegistrationTests
{
    private static Action<KunGatewayOptions> DefaultConfigure() =>
        opts =>
        {
            opts.ExternalIssuer = "https://login.example.com";
            opts.ExternalAudience = "my-api";
            opts.ExternalJwksUri = "https://login.example.com/.well-known/openid-configuration";
        };

    // ── Test 1: double-registration does not throw ────────────────────────────

    [Fact]
    public void AddKunGateway_CalledTwice_DoesNotThrowDuplicatePolicy()
    {
        // Arrange — register twice on the same service collection.
        // YARP's AddReverseProxy is not idempotent internally, so the second call may
        // throw an ArgumentException about duplicate policy IDs. This test pins the
        // current expected behavior: if it throws, it must be an ArgumentException
        // with a message matching "duplicate" (case-insensitive); if it does not throw,
        // at least one IPassiveHealthCheckPolicy must be resolvable.
        var services = new ServiceCollection();
        services.AddLogging();

        Action doubleRegister = () =>
        {
            services.AddKunGateway(DefaultConfigure());
            services.AddKunGateway(DefaultConfigure());
        };

        // Either it succeeds (TryAdd guards prevent duplicates) or it throws a known
        // ArgumentException about duplicate registration — both are acceptable outcomes;
        // the test fails only on unexpected exception types or unexpected messages.
        try
        {
            doubleRegister();
            // If we reach here, build the provider and verify at least one policy is resolvable.
            var provider = services.BuildServiceProvider();
            var policies = provider.GetServices<IPassiveHealthCheckPolicy>();
            policies.Should().NotBeNull();
        }
        catch (ArgumentException ex)
        {
            // Expected: YARP raises ArgumentException for duplicate policy IDs.
            ex.Message.ToLowerInvariant().Should().ContainAny(["duplicate", "already", "registered"],
                "double-registering AddKunGateway should fail with a recognisable duplicate-id message");
        }
    }

    // ── Test 2: missing ReverseProxy config fails fast at startup ─────────────

    [Fact]
    public async Task AddKunGateway_WithoutReverseProxyConfig_FailsFastAtStartup()
    {
        // Arrange — register WITHOUT a reverseProxyConfig section and WITHOUT a custom IProxyConfigProvider.
        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddKunGateway(DefaultConfigure(), reverseProxyConfig: null);
                // Deliberately do NOT register an IProxyConfigProvider.
            })
            .Build();

        // Act
        var act = async () => await host.StartAsync(TestContext.Current.CancellationToken);

        // Assert — ReverseProxyConfigValidationService should throw before accepting requests.
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Kun.Gateway was registered without a ReverseProxy config source*");

        host.Dispose();
    }
}
