using System.Reflection;
using Kun.Observability.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Kun.Observability.Extensions;

/// <summary>Service-collection extensions that register unified Kun observability.</summary>
public static class KunObservabilityServiceCollectionExtensions
{
    /// <summary>
    /// Registers traces, metrics, and logs via OpenTelemetry with all
    /// Kun ActivitySources pre-wired and OTLP export enabled. Safe to
    /// call multiple times against the same service collection — the
    /// second invocation is a no-op guarded by an internal marker.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to configure.</param>
    /// <param name="configure">Optional callback to customize <see cref="KunObservabilityOptions"/>.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddKunObservability(
        this IServiceCollection services,
        Action<KunObservabilityOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Idempotency: subsequent calls only re-apply the options so callers
        // that forward-configure before picking up a prior caller's wiring
        // keep a consistent final state without double-registering exporters.
        if (configure is not null)
        {
            services.Configure(configure);
        }
        else
        {
            services.AddOptions<KunObservabilityOptions>();
        }

        if (services.Any(d => d.ServiceType == typeof(KunObservabilityMarker)))
        {
            return services;
        }
        services.AddSingleton<KunObservabilityMarker>(KunObservabilityMarker.Instance);

        var snapshot = new KunObservabilityOptions();
        configure?.Invoke(snapshot);
        var resolvedServiceName = string.IsNullOrWhiteSpace(snapshot.ServiceName)
            ? Assembly.GetEntryAssembly()?.GetName().Name ?? "kun-service"
            : snapshot.ServiceName;

        var otelBuilder = services.AddOpenTelemetry()
            .ConfigureResource(resource =>
            {
                if (snapshot.ServiceVersion is { Length: > 0 })
                {
                    resource.AddService(resolvedServiceName, serviceVersion: snapshot.ServiceVersion);
                }
                else
                {
                    resource.AddService(resolvedServiceName);
                }
            });

        if (snapshot.TracingEnabled)
        {
            otelBuilder.WithTracing(tracing =>
            {
                foreach (var source in KunActivitySources.All)
                {
                    tracing.AddSource(source);
                }

                if (snapshot.IncludeAspNetCore)
                {
                    tracing.AddAspNetCoreInstrumentation();
                }
                if (snapshot.IncludeHttpClient)
                {
                    tracing.AddHttpClientInstrumentation();
                }
                if (snapshot.IncludeEfCore)
                {
                    tracing.AddEntityFrameworkCoreInstrumentation();
                }

                tracing.AddOtlpExporter(ConfigureOtlp(snapshot));
            });
        }

        if (snapshot.MetricsEnabled)
        {
            otelBuilder.WithMetrics(metrics =>
            {
                if (snapshot.IncludeAspNetCore)
                {
                    metrics.AddAspNetCoreInstrumentation();
                }
                if (snapshot.IncludeHttpClient)
                {
                    metrics.AddHttpClientInstrumentation();
                }
                if (snapshot.IncludeRuntime)
                {
                    metrics.AddRuntimeInstrumentation();
                }

                metrics.AddOtlpExporter(ConfigureOtlp(snapshot));
            });
        }

        if (snapshot.LoggingEnabled)
        {
            services.AddLogging(logging =>
            {
                logging.AddOpenTelemetry(options =>
                {
                    options.IncludeFormattedMessage = true;
                    options.IncludeScopes = true;
                    options.ParseStateValues = true;
                    options.AddOtlpExporter(ConfigureOtlp(snapshot));
                });
            });
        }

        return services;
    }

    private static Action<OpenTelemetry.Exporter.OtlpExporterOptions> ConfigureOtlp(KunObservabilityOptions options)
        => otlp =>
        {
            if (options.OtlpEndpoint is not null)
            {
                otlp.Endpoint = options.OtlpEndpoint;
            }
        };

    private sealed class KunObservabilityMarker
    {
        public static readonly KunObservabilityMarker Instance = new();
        private KunObservabilityMarker() { }
    }
}
