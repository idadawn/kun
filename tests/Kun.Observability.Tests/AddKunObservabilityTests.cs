using System.Diagnostics;
using FluentAssertions;
using Kun.Observability.Abstractions;
using Kun.Observability.Extensions;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Trace;
using Xunit;

namespace Kun.Observability.Tests;

public sealed class AddKunObservabilityTests
{
    private const string TestActivitySourceName = "Kun.Mediator";

    [Fact]
    public void Resource_IncludesServiceNameAndVersion()
    {
        using var provider = BuildHost(o =>
        {
            o.ServiceName = "test-svc";
            o.ServiceVersion = "1.2.3";
            o.IncludeAspNetCore = false;
            o.IncludeHttpClient = false;
            o.IncludeEfCore = false;
            o.IncludeRuntime = false;
            o.MetricsEnabled = false;
            o.LoggingEnabled = false;
        });

        var tracer = provider.GetRequiredService<TracerProvider>();
        var attributes = tracer.GetResource().Attributes.ToDictionary(kv => kv.Key, kv => kv.Value);

        attributes.Should().ContainKey("service.name").WhoseValue.Should().Be("test-svc");
        attributes.Should().ContainKey("service.version").WhoseValue.Should().Be("1.2.3");
    }

    [Fact]
    public void Resource_OmitsServiceVersion_WhenNotProvided()
    {
        using var provider = BuildHost(o =>
        {
            o.ServiceName = "no-version";
            o.IncludeAspNetCore = false;
            o.IncludeHttpClient = false;
            o.IncludeEfCore = false;
            o.IncludeRuntime = false;
            o.MetricsEnabled = false;
            o.LoggingEnabled = false;
        });

        var tracer = provider.GetRequiredService<TracerProvider>();
        var attributes = tracer.GetResource().Attributes.ToDictionary(kv => kv.Key, kv => kv.Value);

        attributes.Should().ContainKey("service.name").WhoseValue.Should().Be("no-version");
        attributes.Should().NotContainKey("service.version");
    }

    [Fact]
    public void TracingEnabledTrue_CapturesKunMediatorSpans()
    {
        var captured = new List<Activity>();

        using var provider = BuildHost(
            configure: o =>
            {
                o.ServiceName = "captures";
                o.IncludeAspNetCore = false;
                o.IncludeHttpClient = false;
                o.IncludeEfCore = false;
                o.IncludeRuntime = false;
                o.MetricsEnabled = false;
                o.LoggingEnabled = false;
            },
            configureTracing: t => t.AddInMemoryExporter(captured));

        using (var source = new ActivitySource(TestActivitySourceName))
        using (var activity = source.StartActivity("unit-test-span"))
        {
            activity.Should().NotBeNull("the AddKunObservability pipeline subscribed to Kun.Mediator");
        }

        captured.Should().ContainSingle(a => a.OperationName == "unit-test-span");
    }

    [Fact]
    public void TracingEnabledFalse_YieldsZeroSpans()
    {
        var captured = new List<Activity>();

        using var provider = BuildHost(
            configure: o =>
            {
                o.ServiceName = "disabled-tracing";
                o.TracingEnabled = false;
                o.MetricsEnabled = false;
                o.LoggingEnabled = false;
            },
            configureTracing: t => t.AddInMemoryExporter(captured));

        using (var source = new ActivitySource(TestActivitySourceName))
        using (var _ = source.StartActivity("should-not-capture"))
        {
            // When TracingEnabled=false we don't call .WithTracing, so no listener subscribes
            // to Kun.Mediator, so StartActivity returns null and no span is emitted.
        }

        captured.Should().BeEmpty();
    }

    [Fact]
    public void AddKunObservability_IsIdempotent()
    {
        var services = new ServiceCollection();
        services.AddKunObservability(o => o.ServiceName = "first");
        services.AddKunObservability(o => o.ServiceName = "second");

        using var provider = services.BuildServiceProvider();

        // Only one TracerProvider registration comes from the single AddOpenTelemetry call.
        provider.GetServices<TracerProvider>().Should().ContainSingle();

        // The first-call options are what actually drove the pipeline build; the second
        // call is a no-op guarded by the internal marker. Freezing on the first call is
        // deliberate — it keeps the pipeline deterministic for consumers that compose
        // libraries calling AddKunObservability themselves.
        var tracer = provider.GetRequiredService<TracerProvider>();
        var attributes = tracer.GetResource().Attributes.ToDictionary(kv => kv.Key, kv => kv.Value);
        attributes["service.name"].Should().Be("first");
    }

    private static ServiceProvider BuildHost(
        Action<KunObservabilityOptions> configure,
        Action<TracerProviderBuilder>? configureTracing = null)
    {
        var services = new ServiceCollection();
        services.AddKunObservability(configure);
        if (configureTracing is not null)
        {
            services.ConfigureOpenTelemetryTracerProvider(configureTracing);
        }
        var provider = services.BuildServiceProvider();
        // Force the hosted service that starts the OTel pipeline to initialize now —
        // without a running host the TracerProvider must be resolved explicitly so the
        // InMemoryExporter is actually attached before activities are emitted. When
        // TracingEnabled=false the provider is intentionally absent, so use GetService.
        _ = provider.GetService<TracerProvider>();
        return provider;
    }
}
