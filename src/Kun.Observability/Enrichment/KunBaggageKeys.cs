namespace Kun.Observability.Enrichment;

/// <summary>
/// Canonical baggage key names the Kun activity enricher mirrors
/// onto Kun-sourced span tags at <c>Activity.Start</c> time. See
/// <c>docs/architecture/ADR-013-Kun.Observability-enricher.md</c>
/// for the contract, including why only these keys are whitelisted
/// and how consumers should populate them.
/// </summary>
/// <remarks>
/// Names follow OpenTelemetry semantic-convention style
/// (<c>namespace.attribute</c>) so that downstream exporters / UIs
/// can correlate them with existing OTel attributes.
/// </remarks>
public static class KunBaggageKeys
{
    /// <summary>Tenant identifier (typically the <c>tenant_id</c> claim from an S2S JWT).</summary>
    public const string TenantId = "tenant.id";

    /// <summary>Authenticated user identifier (typically the <c>sub</c> claim).</summary>
    public const string UserId = "user.id";

    /// <summary>Outbox message identifier for spans emitted during outbox publishing.</summary>
    public const string OutboxMessageId = "outbox.message_id";

    /// <summary>Every whitelisted key — iterate when building the mirror set.</summary>
    public static readonly string[] All = [TenantId, UserId, OutboxMessageId];
}
