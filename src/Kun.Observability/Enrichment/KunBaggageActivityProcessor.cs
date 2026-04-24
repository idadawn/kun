using System.Diagnostics;
using OpenTelemetry;

namespace Kun.Observability.Enrichment;

/// <summary>
/// <see cref="BaseProcessor{T}"/> that mirrors whitelisted
/// <see cref="Activity.Baggage"/> entries (see
/// <see cref="KunBaggageKeys.All"/>) onto span tags when the activity
/// is emitted by a Kun-owned <see cref="ActivitySource"/> (see
/// <see cref="KunActivitySources"/>). Non-Kun activities pass through
/// untouched so built-in instrumentation (ASP.NET Core / HttpClient /
/// EF Core) keeps its own tagging contract.
/// </summary>
/// <remarks>
/// Installed when
/// <c>KunObservabilityOptions.EnrichActivitiesFromBaggage</c> is set
/// to <see langword="true"/>. See
/// <c>docs/architecture/ADR-013-Kun.Observability-enricher.md</c>
/// for the full rationale — why baggage (AsyncLocal-safe) over
/// scoped DI (lifetime mismatch for background-issued activities),
/// why <see cref="BaseProcessor{T}.OnStart"/> (tags land before the
/// exporter serialises the span), and why the key set is
/// deliberately small.
/// </remarks>
internal sealed class KunBaggageActivityProcessor : BaseProcessor<Activity>
{
    private readonly HashSet<string> _kunSources;

    public KunBaggageActivityProcessor()
    {
        _kunSources = new HashSet<string>(KunActivitySources.All, StringComparer.Ordinal);
    }

    public override void OnStart(Activity data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (!_kunSources.Contains(data.Source.Name)) return;

        foreach (var pair in data.Baggage)
        {
            if (pair.Value is null) continue;
            if (!IsWhitelisted(pair.Key)) continue;
            data.SetTag(pair.Key, pair.Value);
        }
    }

    private static bool IsWhitelisted(string key)
    {
        // Linear scan over the tiny KunBaggageKeys.All array is
        // faster (and AOT-friendlier) than allocating a HashSet per
        // instance for three entries.
        for (var i = 0; i < KunBaggageKeys.All.Length; i++)
        {
            if (string.Equals(KunBaggageKeys.All[i], key, StringComparison.Ordinal))
                return true;
        }
        return false;
    }
}
