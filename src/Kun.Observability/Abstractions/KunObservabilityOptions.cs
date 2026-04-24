namespace Kun.Observability.Abstractions;

/// <summary>
/// Configuration for <c>AddKunObservability</c>. Every option has a safe
/// default; the zero-arg call yields a trace + metric + log pipeline with
/// ASP.NET Core + HttpClient + EF Core instrumentation enabled and OTLP
/// export pointed at <c>OTEL_EXPORTER_OTLP_ENDPOINT</c>.
/// </summary>
public sealed class KunObservabilityOptions
{
    /// <summary>
    /// Logical service name reported as the <c>service.name</c> resource
    /// attribute. Falls back to the entry-assembly name when left as an
    /// empty string; supply a stable value for production deployments.
    /// </summary>
    public string ServiceName { get; set; } = string.Empty;

    /// <summary>
    /// Service version reported as <c>service.version</c>. Leave
    /// <see langword="null"/> to omit the attribute; otherwise pass a
    /// semver / commit SHA / calendar version — whatever your deploy
    /// pipeline stamps in.
    /// </summary>
    public string? ServiceVersion { get; set; }

    /// <summary>
    /// Override for the OTLP endpoint URL. When <see langword="null"/>
    /// (default), the OpenTelemetry SDK reads
    /// <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> from the environment, matching
    /// the .NET Aspire convention.
    /// </summary>
    public Uri? OtlpEndpoint { get; set; }

    /// <summary>When <see langword="true"/> (default), captures .NET runtime metrics (GC, threadpool, contention).</summary>
    public bool IncludeRuntime { get; set; } = true;

    /// <summary>When <see langword="true"/> (default), captures EF Core SQL command traces.</summary>
    public bool IncludeEfCore { get; set; } = true;

    /// <summary>When <see langword="true"/> (default), captures outbound <c>HttpClient</c> traces and metrics.</summary>
    public bool IncludeHttpClient { get; set; } = true;

    /// <summary>When <see langword="true"/> (default), captures ASP.NET Core server traces and metrics.</summary>
    public bool IncludeAspNetCore { get; set; } = true;

    /// <summary>When <see langword="true"/> (default), registers the tracing pipeline and Kun ActivitySources.</summary>
    public bool TracingEnabled { get; set; } = true;

    /// <summary>When <see langword="true"/> (default), registers the metrics pipeline.</summary>
    public bool MetricsEnabled { get; set; } = true;

    /// <summary>When <see langword="true"/> (default), registers the OpenTelemetry logger provider and exports log records via OTLP.</summary>
    public bool LoggingEnabled { get; set; } = true;

    /// <summary>
    /// When <see langword="true"/>, installs a
    /// <c>BaseProcessor&lt;Activity&gt;</c> that mirrors whitelisted
    /// OpenTelemetry baggage entries (see
    /// <c>Kun.Observability.Enrichment.KunBaggageKeys</c>) onto span
    /// tags for activities emitted by Kun-owned
    /// <see cref="System.Diagnostics.ActivitySource"/>s. Default
    /// <see langword="false"/> (opt-in per
    /// <c>docs/architecture/ADR-013-Kun.Observability-enricher.md</c>
    /// §Default posture). Has no effect when
    /// <see cref="TracingEnabled"/> is <see langword="false"/>.
    /// </summary>
    public bool EnrichActivitiesFromBaggage { get; set; }

    /// <summary>
    /// When <see langword="true"/>, installs a
    /// <c>BaseProcessor&lt;LogRecord&gt;</c> that mirrors whitelisted
    /// OpenTelemetry baggage entries (from the ambient
    /// <see cref="System.Diagnostics.Activity.Current"/>) onto log
    /// record attributes. The log statement's own attributes win
    /// on conflict — the enricher only adds missing keys. Default
    /// <see langword="false"/> (opt-in, mirrors
    /// <see cref="EnrichActivitiesFromBaggage"/> posture per
    /// ADR-013 §F1 resolution). Has no effect when
    /// <see cref="LoggingEnabled"/> is <see langword="false"/>.
    /// </summary>
    public bool EnrichLogsFromBaggage { get; set; }
}
