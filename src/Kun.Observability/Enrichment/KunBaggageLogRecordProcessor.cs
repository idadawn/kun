using System.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Logs;

namespace Kun.Observability.Enrichment;

/// <summary>
/// <see cref="BaseProcessor{T}"/> that mirrors whitelisted
/// <see cref="Activity.Baggage"/> entries (see
/// <see cref="KunBaggageKeys.All"/>) onto a <see cref="LogRecord"/>'s
/// attributes when the log call lands inside an active
/// <see cref="Activity"/>. If the log statement already set a given
/// whitelisted key, the caller's value wins — the enricher adds
/// only missing keys.
/// </summary>
/// <remarks>
/// Installed when
/// <c>KunObservabilityOptions.EnrichLogsFromBaggage</c> is set to
/// <see langword="true"/>. See
/// <c>docs/architecture/ADR-013-Kun.Observability-enricher.md</c>
/// §F1 resolution for the full rationale — companion to the
/// <see cref="KunBaggageActivityProcessor"/>.
/// </remarks>
internal sealed class KunBaggageLogRecordProcessor : BaseProcessor<LogRecord>
{
    public override void OnEnd(LogRecord data)
    {
        ArgumentNullException.ThrowIfNull(data);

        var activity = Activity.Current;
        if (activity is null) return;

        List<KeyValuePair<string, object?>>? updated = null;

        foreach (var pair in activity.Baggage)
        {
            if (pair.Value is null) continue;
            if (!IsWhitelisted(pair.Key)) continue;
            if (AlreadyHasAttribute(data.Attributes, pair.Key)) continue;

            updated ??= new List<KeyValuePair<string, object?>>(
                data.Attributes ?? []);
            updated.Add(new KeyValuePair<string, object?>(pair.Key, pair.Value));
        }

        if (updated is not null)
        {
            data.Attributes = updated;
        }
    }

    private static bool IsWhitelisted(string key)
    {
        for (var i = 0; i < KunBaggageKeys.All.Length; i++)
        {
            if (string.Equals(KunBaggageKeys.All[i], key, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private static bool AlreadyHasAttribute(
        IReadOnlyList<KeyValuePair<string, object?>>? existing,
        string key)
    {
        if (existing is null) return false;
        for (var i = 0; i < existing.Count; i++)
        {
            if (string.Equals(existing[i].Key, key, StringComparison.Ordinal))
                return true;
        }
        return false;
    }
}
