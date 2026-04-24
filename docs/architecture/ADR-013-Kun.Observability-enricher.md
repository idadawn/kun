# ADR-013: Baggage-driven activity enricher for `Kun.Observability`

**Status**: Accepted
**Date**: 2026-04-24
**Author**: claude-kun (Kun framework specialist)
**Extends**: [ADR-004](./ADR-004-Kun.Observability-module.md) — *Kun.Observability module decision*
**Supersedes**: none

**Context**: `KunActivitySources` pre-registers the four framework
sources (`Kun.Mediator`, `Kun.EventBus.Outbox`, `Kun.Grpc`,
`Kun.Saga`). Spans from those sources reach the Aspire / Jaeger UI
correctly, but carry no `tenant.id` / `user.id` tags. Operators
filtering by tenant or correlating spans to application logs end up
doing the join by hand every time. The information *exists* —
`ITenantContext` knows the tenant, `ICurrentUser` knows the user, the
Outbox envelope carries a `message_id` — but there's no wired path
from those sources to span tags.

---

## Decision

Read enrichment values from OpenTelemetry **baggage** rather than
from scoped DI services, and mirror a whitelisted set of baggage
keys onto span tags at `Activity` start time via a
`BaseProcessor<Activity>` registered into the `TracerProviderBuilder`.

Consumer code that has tenant/user/outbox info pushes the values into
baggage once (usually at the request entry point — `Kun.Gateway`
already does this for the S2S JWT tenant claim via an
`IEndpointFilter`), and the baggage rides the ambient
`AsyncLocal<>`-backed context across HTTP / gRPC / async hops,
reaching every downstream Activity without additional wiring.

### Hook point: `OnEnd`, not `OnStart`

The mirror happens in `BaseProcessor<Activity>.OnEnd`, not
`OnStart`. Rationale — `OnStart` runs the instant `Activity.Start()`
is called, which is *before* consumer code inside the activity
body has a chance to call `SetBaggage` on it. `OnEnd` runs just
before the activity leaves the pipeline toward exporters and sees
every baggage entry that was set at any point in the activity's
lifespan. This handles both idiomatic cases cleanly:

1. Baggage set at request boundary → child activity inherits it →
   `OnEnd` mirrors it to tags.
2. Baggage set mid-activity (less common but legal) → `OnEnd` still
   sees the final baggage state and mirrors it.

Exporters receive the tagged activity on the same pipeline pass
because `OnEnd` precedes the exporter call in OTel's processor
chain.

### Why baggage and not scoped DI

The intuitive design — "processor constructor takes
`IServiceProvider`, resolves `ITenantContext` on each `OnStart`" —
breaks for three reasons:

1. `BaseProcessor<Activity>` instances are singletons in the OTel
   pipeline. Resolving scoped services from the root provider
   throws at runtime.
2. Even with `IServiceScopeFactory`, the processor has no way to
   know *which* scope to resolve against. The activity being
   started may have been queued by a background service whose scope
   closed before the activity reaches the exporter.
3. Background contexts (Saga executor, Outbox publisher, timers)
   either have no DI scope or have a different scope than the
   originating request. Trying to thread the "right" scope through
   every activity-emitting code path is exactly the hand-stitching
   this ADR is meant to eliminate.

Baggage sidesteps all three. It is the W3C-standard async-propagated
context slot. `Activity.Current?.Baggage` is always accessible, no
DI needed, no lifetime confusion.

### Canonical baggage keys

```csharp
public static class KunBaggageKeys
{
    public const string TenantId         = "tenant.id";
    public const string UserId           = "user.id";
    public const string OutboxMessageId  = "outbox.message_id";
}
```

Names follow OpenTelemetry semconv naming (`namespace.attribute`).
Other baggage keys present on the activity are **ignored** — the
processor copies only the whitelisted set. Consumers that want
additional tags add them at the code site that already has the
value; they do not register a new enricher.

### Default posture: opt-in

New flag `KunObservabilityOptions.EnrichActivitiesFromBaggage`,
**default `false`**. Two reasons for opt-in:

- Adds a processor to the tracing pipeline and thus a few
  nanoseconds per span. Negligible in practice but we keep
  observability features additive and measurable.
- Baggage can carry values a downstream operator did not expect to
  see exported (e.g., sensitive PII a consumer service pushed in a
  custom key). Although this processor copies only the whitelisted
  keys, explicit opt-in forces callers to think about the baggage
  contract their service is producing.

Downstream services enable the flag in their
`builder.Services.AddKunObservability(o => o.EnrichActivitiesFromBaggage = true)`
call.

### Processor scope

The processor operates only on activities whose `ActivitySource.Name`
is in `KunActivitySources.All` — framework-owned spans. ASP.NET Core
/ HttpClient / EF Core instrumentation spans are handled by their
respective built-in enrichers and should not be touched by this one.

### AOT safety

`BaseProcessor<Activity>` is an abstract class with no generic
constraints; extending it is AOT-safe. `Activity.Baggage` returns a
non-generic `IEnumerable<KeyValuePair<string, string?>>` which
materialises via the BCL without reflection. No `MakeGenericType`,
no `Expression` trees. The existing
`<IsAotCandidate>true</IsAotCandidate>` on `Kun.Observability` stays
unchanged.

### What it is **not**

- **Not a log-record enricher.** `OpenTelemetry.Logs.LogRecord` has a
  separate `AttributeEnricher` surface — that's F1.
- **Not a metric-dimension enricher.** Most Kun metrics are counters
  without labels today; adding dimensions is premature. F-out.
- **Not a replacement for explicit, code-site tagging.** Handlers
  that know they just processed a specific resource (`order.id`,
  `saga.instance_id`) should still call
  `Activity.Current?.SetTag("order.id", id)` at the site — baggage
  is for cross-cutting dimensions, not per-entity identifiers.

---

## Alternatives considered

### Capture `IServiceScopeFactory` and resolve per-activity

Rejected — see §Why baggage and not scoped DI above. The scope
uncertainty makes this design incorrect for background-issued
activities.

### `ActivityListener` registered globally

Equivalent capability but `ActivityListener` is less structured than
a `BaseProcessor` and cannot participate cleanly in the
`TracerProviderBuilder` lifecycle; two callbacks (`ActivityStarted`,
`ActivityStopped`) duplicate the `BaseProcessor` model without
benefit. Chose `BaseProcessor<Activity>` for consistency with the
rest of the OTel SDK wiring.

### Force every framework component to call `Activity.SetTag`

Rejected. That's exactly the "Mediator behaviour / Outbox publisher /
Saga executor / Grpc interceptor all know how to tag tenant_id"
hand-stitching we're replacing. Any future activity-emitting site
would have to remember the incantation.

### Full `IKunActivityEnricher` pluggable interface + registry

Considered as a larger surface. Rejected for this ADR's scope — the
built-in baggage-to-tags mapper covers the known need; adding a
pluggable enricher abstraction should wait until a concrete second
implementation appears. YAGNI, revisit if consumer service demands a
custom enricher that can't be expressed as "push this value into
baggage".

---

## Follow-ups

- **F1**: Log-record enricher that mirrors the same baggage keys
  onto `LogRecord.Attributes`. Separate ADR because the
  OpenTelemetry.Logs SDK surface differs from Activity.
- **F2**: Expose baggage-tagging demo in `Kun.Sample.AppHost`
  Aspire dashboard when the samples move beyond placeholder.
- **F3**: If a concrete consumer need surfaces for a non-baggage
  enrichment source (e.g., reading from a distributed cache or an
  S2S JWT without baggage plumbing), promote to a pluggable
  `IKunActivityEnricher` abstraction. Until then, this ADR is the
  complete surface.

---

## Impact on existing code

Purely additive. `AddKunObservability` gains one optional flag
(default off). `Kun.Observability.csproj` takes no new
`PackageReference`. Existing Aspire / OTLP pipelines remain
unchanged for consumers that do not flip the opt-in flag.
