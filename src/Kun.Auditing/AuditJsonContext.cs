using System.Text.Json.Serialization;
using Kun.Auditing.Events;

namespace Kun.Auditing;

/// <summary>
/// Source-generated <see cref="JsonSerializerContext"/> for AOT-safe serialization of
/// <see cref="AuditEventIntegrationEvent"/>. Kept <c>internal</c> on purpose: the
/// framework publishes audit events through the outbox write path and only needs the
/// type info internally; external consumers wiring custom sinks should define their own
/// <see cref="JsonSerializerContext"/> and register it via their consumer-side DI.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(AuditEventIntegrationEvent))]
internal sealed partial class AuditJsonContext : JsonSerializerContext;
