using System.Diagnostics;
using FluentAssertions;
using Kun.Observability.Enrichment;
using Kun.Observability.Extensions;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Trace;
using Xunit;

namespace Kun.Observability.Tests;

public sealed class BaggageEnrichmentTests
{
    private const string KunSource = "Kun.Mediator";
    private const string NonKunSource = "Test.NonKun";

    // ActivitySource listening is process-global: other test classes in this
    // assembly run in parallel and also emit on Kun.Mediator, so their spans
    // can land in this class's in-memory exporter. Assertions must therefore
    // select by operation name instead of assuming the export list holds
    // exactly one entry.
    private static Activity SingleExported(List<Activity> exported, string operationName)
        => exported.Should().ContainSingle(a => a.OperationName == operationName).Which;

    public BaggageEnrichmentTests()
    {
        // Other tests in this assembly may have left an Activity on the
        // AsyncLocal context; without resetting we inherit its trace
        // flags and may short-circuit StartActivity in this test.
        Activity.Current = null;
    }

    // ── Processor-level unit tests (manual TracerProvider) ──────────────────

    [Fact]
    public void Processor_OnKunSourcedActivity_WithTenantBaggage_CopiesToSpanTag()
    {
        var exported = new List<Activity>();
        using var provider = BuildManualProvider(exported, extraSource: null);
        using var source = new ActivitySource(KunSource);

        using (var activity = source.StartActivity("kun.op"))
        {
            activity.Should().NotBeNull();
            activity!.SetBaggage(KunBaggageKeys.TenantId, "tenant-42");
        }

        var activityOut = SingleExported(exported, "kun.op");
        activityOut.GetTagItem(KunBaggageKeys.TenantId).Should().Be("tenant-42");
    }

    [Fact]
    public void Processor_OnNonKunActivity_DoesNotCopyBaggageToTag()
    {
        var exported = new List<Activity>();
        using var provider = BuildManualProvider(exported, extraSource: NonKunSource);
        using var source = new ActivitySource(NonKunSource);

        using (var activity = source.StartActivity("nonkun.op"))
        {
            activity.Should().NotBeNull();
            activity!.SetBaggage(KunBaggageKeys.TenantId, "tenant-42");
        }

        var activityOut = SingleExported(exported, "nonkun.op");
        activityOut.GetTagItem(KunBaggageKeys.TenantId).Should().BeNull();
    }

    [Fact]
    public void Processor_IgnoresNonWhitelistedBaggageKey()
    {
        var exported = new List<Activity>();
        using var provider = BuildManualProvider(exported, extraSource: null);
        using var source = new ActivitySource(KunSource);

        using (var activity = source.StartActivity("kun.op"))
        {
            activity.Should().NotBeNull();
            activity!.SetBaggage("custom.noise", "secret");
        }

        var activityOut = SingleExported(exported, "kun.op");
        activityOut.GetTagItem("custom.noise").Should().BeNull();
    }

    [Fact]
    public void Processor_CopiesMultipleWhitelistedKeys()
    {
        var exported = new List<Activity>();
        using var provider = BuildManualProvider(exported, extraSource: null);
        using var source = new ActivitySource(KunSource);

        using (var activity = source.StartActivity("kun.op"))
        {
            activity.Should().NotBeNull();
            activity!.SetBaggage(KunBaggageKeys.TenantId, "t-1");
            activity.SetBaggage(KunBaggageKeys.UserId, "u-9");
        }

        var activityOut = SingleExported(exported, "kun.op");
        activityOut.GetTagItem(KunBaggageKeys.TenantId).Should().Be("t-1");
        activityOut.GetTagItem(KunBaggageKeys.UserId).Should().Be("u-9");
    }

    // ── AddKunObservability wiring ──────────────────────────────────────────

    [Fact]
    public void AddKunObservability_WithFlagFalse_DoesNotCopyBaggage()
    {
        var exported = new List<Activity>();
        using var provider = BuildHostedProvider(exported, enrich: false);
        _ = provider.GetRequiredService<TracerProvider>(); // force pipeline build
        using var source = new ActivitySource(KunSource);

        using (var activity = source.StartActivity("kun.op"))
        {
            activity.Should().NotBeNull();
            activity!.SetBaggage(KunBaggageKeys.TenantId, "tenant-42");
        }

        provider.GetRequiredService<TracerProvider>().ForceFlush();

        var activityOut = SingleExported(exported, "kun.op");
        activityOut.GetTagItem(KunBaggageKeys.TenantId).Should().BeNull();
    }

    [Fact]
    public void AddKunObservability_WithFlagTrue_CopiesBaggage()
    {
        var exported = new List<Activity>();
        using var provider = BuildHostedProvider(exported, enrich: true);
        _ = provider.GetRequiredService<TracerProvider>(); // force pipeline build
        using var source = new ActivitySource(KunSource);

        using (var activity = source.StartActivity("kun.op"))
        {
            activity.Should().NotBeNull();
            activity!.SetBaggage(KunBaggageKeys.TenantId, "tenant-42");
        }

        provider.GetRequiredService<TracerProvider>().ForceFlush();

        var activityOut = SingleExported(exported, "kun.op");
        activityOut.GetTagItem(KunBaggageKeys.TenantId).Should().Be("tenant-42");
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    private static TracerProvider BuildManualProvider(List<Activity> exported, string? extraSource)
    {
        // AlwaysOnSampler keeps the sample decision deterministic when
        // tests run in the same AppDomain as others that may have left
        // parent-based samplers on Activity.Current.
        var builder = Sdk.CreateTracerProviderBuilder()
            .SetSampler(new AlwaysOnSampler())
            .AddSource(KunSource);

        if (extraSource is not null)
        {
            builder = builder.AddSource(extraSource);
        }

        return builder
            .AddProcessor(new KunBaggageActivityProcessor())
            .AddInMemoryExporter(exported)
            .Build()!;
    }

    private static ServiceProvider BuildHostedProvider(List<Activity> exported, bool enrich)
    {
        var services = new ServiceCollection();
        services.AddKunObservability(o =>
        {
            o.ServiceName = "baggage-test";
            o.IncludeAspNetCore = false;
            o.IncludeHttpClient = false;
            o.IncludeEfCore = false;
            o.IncludeRuntime = false;
            o.MetricsEnabled = false;
            o.LoggingEnabled = false;
            o.EnrichActivitiesFromBaggage = enrich;
        });
        // AddKunObservability does not supply a default exporter we can
        // inspect in-process; bolt an InMemory exporter and a
        // deterministic sampler onto the same TracerProviderBuilder via
        // the hosted-services extension point. AlwaysOnSampler isolates
        // this test from parent-based sampling decisions left over by
        // other TracerProvider instances in the same AppDomain.
        services.ConfigureOpenTelemetryTracerProvider(t => t
            .SetSampler(new AlwaysOnSampler())
            .AddInMemoryExporter(exported));

        return services.BuildServiceProvider();
    }
}
