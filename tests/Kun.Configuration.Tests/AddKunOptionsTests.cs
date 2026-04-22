using System.Diagnostics.CodeAnalysis;
using FluentAssertions;
using Kun.Configuration.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace Kun.Configuration.Tests;

[RequiresUnreferencedCode("Tests intentionally exercise reflection-based options validation.")]
[RequiresDynamicCode("Tests intentionally exercise reflection-based options validation.")]
public sealed class AddKunOptionsTests
{
    [Fact]
    public async Task Happy_ValidConfiguration_BindsAndValidates()
    {
        using var host = BuildHost<AuthentikOptions>("Authentik", new Dictionary<string, string?>
        {
            ["Authentik:Authority"] = "https://idp.test",
            ["Authentik:ClientId"] = "kun-api",
            ["Authentik:ClientSecret"] = "shhh",
        });

        var ct = TestContext.Current.CancellationToken;
        await host.StartAsync(ct);
        try
        {
            var opts = host.Services.GetRequiredService<IOptions<AuthentikOptions>>().Value;
            opts.Authority.Should().Be("https://idp.test");
            opts.ClientId.Should().Be("kun-api");
            opts.ClientSecret.Should().Be("shhh");
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    [Fact]
    public async Task MissingRequiredKey_FailsAtStartup_WithSectionPath()
    {
        using var host = BuildHost<AuthentikOptions>("Authentik", new Dictionary<string, string?>
        {
            ["Authentik:Authority"] = "https://idp.test",
            // ClientId missing
            ["Authentik:ClientSecret"] = "shhh",
        });
        var ct = TestContext.Current.CancellationToken;

        var act = async () => await host.StartAsync(ct);

        var ex = (await act.Should().ThrowAsync<OptionsValidationException>()).Which;
        ex.Failures.Should().Contain(f => f.Contains("Authentik:ClientId", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MalformedUrl_FailsAtStartup_WithFieldName()
    {
        using var host = BuildHost<AuthentikOptions>("Authentik", new Dictionary<string, string?>
        {
            ["Authentik:Authority"] = "not-a-url",
            ["Authentik:ClientId"] = "kun-api",
            ["Authentik:ClientSecret"] = "shhh",
        });
        var ct = TestContext.Current.CancellationToken;

        var act = async () => await host.StartAsync(ct);

        var ex = (await act.Should().ThrowAsync<OptionsValidationException>()).Which;
        ex.Failures.Should().Contain(f => f.Contains("Authentik:Authority", StringComparison.Ordinal)
                                       && f.Contains("absolute URL", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task NestedSection_BindsAndValidates()
    {
        using var host = BuildHost<NestedDatabaseOptions>("Database:Primary", new Dictionary<string, string?>
        {
            ["Database:Primary:ConnectionString"] = "Host=localhost;Database=test",
            ["Database:Primary:DashboardUrl"] = "https://pgadmin.test",
        });
        var ct = TestContext.Current.CancellationToken;

        await host.StartAsync(ct);
        try
        {
            var opts = host.Services.GetRequiredService<IOptions<NestedDatabaseOptions>>().Value;
            opts.ConnectionString.Should().Be("Host=localhost;Database=test");
            opts.DashboardUrl.Should().Be("https://pgadmin.test");
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    [Fact]
    public async Task AggregateFailures_AllReported()
    {
        using var host = BuildHost<AggregateOptions>("Agg", new Dictionary<string, string?>
        {
            // First and Second both missing; Third set to whitespace
            ["Agg:Third"] = "   ",
        });
        var ct = TestContext.Current.CancellationToken;

        var act = async () => await host.StartAsync(ct);

        var ex = (await act.Should().ThrowAsync<OptionsValidationException>()).Which;
        ex.Failures.Should().HaveCountGreaterThanOrEqualTo(3);
        ex.Failures.Should().Contain(f => f.Contains("Agg:First", StringComparison.Ordinal));
        ex.Failures.Should().Contain(f => f.Contains("Agg:Second", StringComparison.Ordinal));
        ex.Failures.Should().Contain(f => f.Contains("Agg:Third", StringComparison.Ordinal));
    }

    [Fact]
    public async Task NonEmptyString_WhitespaceOnly_Rejected()
    {
        using var host = BuildHost<AuthentikOptions>("Authentik", new Dictionary<string, string?>
        {
            ["Authentik:Authority"] = "https://idp.test",
            ["Authentik:ClientId"] = "   ",
            ["Authentik:ClientSecret"] = "shhh",
        });
        var ct = TestContext.Current.CancellationToken;

        var act = async () => await host.StartAsync(ct);

        var ex = (await act.Should().ThrowAsync<OptionsValidationException>()).Which;
        ex.Failures.Should().Contain(f => f.Contains("Authentik:ClientId", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RuntimeGet_DoesNotRevalidate_AfterStartupPassed()
    {
        using var host = BuildHost<AuthentikOptions>("Authentik", new Dictionary<string, string?>
        {
            ["Authentik:Authority"] = "https://idp.test",
            ["Authentik:ClientId"] = "kun-api",
            ["Authentik:ClientSecret"] = "shhh",
        });
        var ct = TestContext.Current.CancellationToken;
        await host.StartAsync(ct);
        try
        {
            var opts = host.Services.GetRequiredService<IOptions<AuthentikOptions>>();

            // Even if we mutate the underlying instance, IOptions is a single shared cached value
            // — reads return the same validated snapshot, no re-validation invoked.
            var first = opts.Value;
            first.ClientId = "   "; // would fail validation if it ran again
            var second = opts.Value;

            second.Should().BeSameAs(first);
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    [Fact]
    public void AddKunOptions_NullSection_Throws()
    {
        var services = new ServiceCollection();
        var act = () => services.AddKunOptions<AuthentikOptions>(string.Empty);
        act.Should().Throw<ArgumentException>();
    }

    private static IHost BuildHost<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TOptions>(
        string section,
        Dictionary<string, string?> config)
        where TOptions : class
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(config);
        builder.Services.AddKunOptions<TOptions>(section);
        return builder.Build();
    }
}
