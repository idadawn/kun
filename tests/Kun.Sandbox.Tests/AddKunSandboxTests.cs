using FluentAssertions;
using Kun.Sandbox.Abstractions;
using Kun.Sandbox.Extensions;
using Kun.Sandbox.Implementations;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Kun.Sandbox.Tests;

public sealed class AddKunSandboxTests
{
    [Fact]
    public void UseInMemory_registers_InMemorySandboxProvider()
    {
        var services = new ServiceCollection();

        services.AddKunSandbox(o => o.UseInMemory());
        using var sp = services.BuildServiceProvider();

        sp.GetRequiredService<ISandboxProvider>().Should().BeOfType<InMemorySandboxProvider>();
    }

    [Fact]
    public void UseSupervisor_registers_SupervisorSandboxProvider_and_wires_HttpClientFactory()
    {
        var services = new ServiceCollection();

        services.AddKunSandbox(o => o.UseSupervisor(new Uri("https://sandbox-supervisor.internal:8443/")));
        using var sp = services.BuildServiceProvider();

        sp.GetRequiredService<ISandboxProvider>().Should().BeOfType<SupervisorSandboxProvider>();
        sp.GetRequiredService<IHttpClientFactory>()
            .CreateClient(SupervisorSandboxProvider.HttpClientName)
            .BaseAddress.Should().Be(new Uri("https://sandbox-supervisor.internal:8443/"));
    }

    [Fact]
    public void UseE2BCloud_registers_E2BCloudSandboxProvider()
    {
        var services = new ServiceCollection();

        services.AddKunSandbox(o => o.UseE2BCloud("api", "team"));
        using var sp = services.BuildServiceProvider();

        sp.GetRequiredService<ISandboxProvider>().Should().BeOfType<E2BCloudSandboxProvider>();
    }

    [Fact]
    public void AddKunSandbox_without_backend_selection_throws()
    {
        var services = new ServiceCollection();

        var act = () => services.AddKunSandbox(_ => { });

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*UseSupervisor*UseE2BCloud*UseInMemory*");
    }

    [Fact]
    public void UseSupervisor_rejects_relative_url()
    {
        var services = new ServiceCollection();

        var act = () => services.AddKunSandbox(o => o.UseSupervisor(new Uri("/relative", UriKind.Relative)));

        act.Should().Throw<ArgumentException>();
    }
}
