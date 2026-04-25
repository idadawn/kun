namespace Kun.Observability;

/// <summary>
/// Canonical list of ActivitySource names owned by the Kun Framework.
/// <c>AddKunObservability</c> subscribes to each of these automatically
/// so consumer services don't have to track new framework sources
/// across upgrades.
/// </summary>
internal static class KunActivitySources
{
    /// <summary>CQRS dispatcher (<c>TracingBehavior</c> spans).</summary>
    public const string Mediator = "Kun.Mediator";

    /// <summary>Outbox publisher background service (<c>outbox.publish</c> spans).</summary>
    public const string OutboxPublisher = "Kun.EventBus.Outbox";

    /// <summary>gRPC server + client interceptors (<c>grpc.server</c> spans).</summary>
    public const string Grpc = "Kun.Grpc";

    /// <summary>Saga orchestrator (<c>saga.step.execute</c> / <c>saga.step.compensate</c> spans).</summary>
    public const string Saga = "Kun.Saga";

    /// <summary>Every canonical source — iterate when wiring up the tracer provider.</summary>
    public static readonly string[] All = [Mediator, OutboxPublisher, Grpc, Saga];
}
