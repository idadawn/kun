// Tests: LoggerMessage source-gen (CA1848) is overkill for in-memory
// verification paths, and the placeholder `{tenant.id}` matches the
// OpenTelemetry semconv key (dot-namespaced lowercase) on purpose —
// PascalCase (CA1727) would break the processor's ordinal match.
#pragma warning disable CA1848, CA1727, CA1873

using System.Diagnostics;
using FluentAssertions;
using Kun.Observability.Enrichment;
using Kun.Observability.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;
using Xunit;

namespace Kun.Observability.Tests;

public sealed class LogBaggageEnrichmentTests
{
    private const string KunSource = "Kun.Mediator";

    public LogBaggageEnrichmentTests()
    {
        // Reset ambient Activity leaked by prior tests — baggage reads from
        // Activity.Current so the starting state needs to be clean.
        Activity.Current = null;
    }

    [Fact]
    public void EnrichLogsFromBaggage_True_WithBaggageOnActivity_AddsAttributeToLogRecord()
    {
        var logRecords = new List<LogRecord>();
        using var provider = BuildLoggerFactory(logRecords, enrich: true);
        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("test");

        using var source = new ActivitySource(KunSource);
        using (var activity = source.StartActivity("span"))
        {
            if (activity is not null)
            {
                activity.SetBaggage(KunBaggageKeys.TenantId, "tenant-42");
            }
            logger.LogInformation("hello");
        }

        provider.GetRequiredService<ILoggerFactory>().Dispose();

        var record = logRecords.Should().ContainSingle().Which;
        GetAttribute(record, KunBaggageKeys.TenantId).Should().Be("tenant-42");
    }

    [Fact]
    public void EnrichLogsFromBaggage_False_WithBaggageOnActivity_DoesNotAddAttribute()
    {
        var logRecords = new List<LogRecord>();
        using var provider = BuildLoggerFactory(logRecords, enrich: false);
        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("test");

        using var source = new ActivitySource(KunSource);
        using (var activity = source.StartActivity("span"))
        {
            activity?.SetBaggage(KunBaggageKeys.TenantId, "tenant-42");
            logger.LogInformation("hello");
        }

        provider.GetRequiredService<ILoggerFactory>().Dispose();

        var record = logRecords.Should().ContainSingle().Which;
        GetAttribute(record, KunBaggageKeys.TenantId).Should().BeNull();
    }

    [Fact]
    public void EnrichLogsFromBaggage_True_RespectsCallerSetAttribute()
    {
        var logRecords = new List<LogRecord>();
        using var provider = BuildLoggerFactory(logRecords, enrich: true);
        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("test");

        using var source = new ActivitySource(KunSource);
        using (var activity = source.StartActivity("span"))
        {
            activity?.SetBaggage(KunBaggageKeys.TenantId, "tenant-from-baggage");
            // Structured logging: the "{tenant.id}" placeholder lands as a
            // log-record attribute with that key. Processor must preserve
            // the caller's explicit value over the baggage mirror.
            logger.LogInformation("tid={tenant.id}", "tenant-from-log-call");
        }

        provider.GetRequiredService<ILoggerFactory>().Dispose();

        var record = logRecords.Should().ContainSingle().Which;
        GetAttribute(record, KunBaggageKeys.TenantId).Should().Be("tenant-from-log-call");
    }

    [Fact]
    public void EnrichLogsFromBaggage_True_WithNoActiveActivity_DoesNotCrashOrAdd()
    {
        var logRecords = new List<LogRecord>();
        using var provider = BuildLoggerFactory(logRecords, enrich: true);
        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("test");

        // No surrounding Activity.StartActivity — log from a bare context.
        Activity.Current.Should().BeNull();
        logger.LogInformation("bare");

        provider.GetRequiredService<ILoggerFactory>().Dispose();

        var record = logRecords.Should().ContainSingle().Which;
        GetAttribute(record, KunBaggageKeys.TenantId).Should().BeNull();
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    private static ServiceProvider BuildLoggerFactory(List<LogRecord> logRecords, bool enrich)
    {
        var services = new ServiceCollection();
        services.AddKunObservability(o =>
        {
            o.ServiceName = "log-baggage-test";
            o.IncludeAspNetCore = false;
            o.IncludeHttpClient = false;
            o.IncludeEfCore = false;
            o.IncludeRuntime = false;
            o.MetricsEnabled = false;
            o.TracingEnabled = true;
            o.LoggingEnabled = true;
            o.EnrichLogsFromBaggage = enrich;
        });
        services.Configure<OpenTelemetryLoggerOptions>(
            o => o.AddInMemoryExporter(logRecords));

        return services.BuildServiceProvider();
    }

    private static object? GetAttribute(LogRecord record, string key)
    {
        if (record.Attributes is null) return null;
        foreach (var pair in record.Attributes)
        {
            if (string.Equals(pair.Key, key, StringComparison.Ordinal))
                return pair.Value;
        }
        return null;
    }
}
