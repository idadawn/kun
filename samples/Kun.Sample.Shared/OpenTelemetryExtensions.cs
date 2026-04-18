using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Kun.Sample.Shared;

/// <summary>Opinionated OpenTelemetry wiring for Kun Framework sample services.</summary>
public static class OpenTelemetryExtensions
{
    /// <summary>
    /// Registers tracing + metrics with OTLP export and all Kun ActivitySources.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to configure.</param>
    /// <param name="serviceName">The logical service name reported to the collector (e.g. "catalog", "order").</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddKunObservability(
        this IServiceCollection services,
        string serviceName)
    {
        services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(serviceName))
            .WithTracing(t => t
                .AddSource("Kun.Mediator")
                .AddSource("Kun.EventBus.Outbox")
                .AddSource("Kun.Grpc")
                .AddSource("Kun.Saga")
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddOtlpExporter())
            .WithMetrics(m => m
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddOtlpExporter());
        return services;
    }
}
