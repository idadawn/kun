using Microsoft.AspNetCore.RateLimiting;

namespace Kun.RateLimit.Abstractions;

/// <summary>
/// Root configuration for <c>AddKunRateLimit</c>. Callers register one
/// or more named windows — typically <c>"api"</c> for bulk read / write
/// traffic and <c>"auth"</c> for stricter login paths — and optionally
/// supply a custom <see cref="OnRejected"/> hook that runs after the
/// default 429 response is written.
/// </summary>
public sealed class KunRateLimitOptions
{
    /// <summary>
    /// Named rate-limit windows. Keys double as the policy names that
    /// consumers attach via <c>.RequireRateLimiting("api")</c>.
    /// </summary>
    public Dictionary<string, WindowRule> Windows { get; } = [];

    /// <summary>
    /// Optional hook invoked after the default 429 / <c>Retry-After</c>
    /// response is written. Ideal for structured logging or metrics
    /// emission; do <b>not</b> mutate the response from here — the
    /// response has already been committed.
    /// </summary>
    public Func<OnRejectedContext, CancellationToken, ValueTask>? OnRejected { get; set; }

    /// <summary>
    /// Registers a new window under <paramref name="name"/>. The builder
    /// returns <see langword="this"/> so callers can chain multiple
    /// <c>AddWindow</c> calls in a single options callback.
    /// </summary>
    /// <param name="name">Policy name — used as the argument to <c>RequireRateLimiting</c>.</param>
    /// <param name="configure">Callback to populate the window's <see cref="WindowRule"/>.</param>
    public KunRateLimitOptions AddWindow(string name, Action<WindowRule> configure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(configure);
        var rule = new WindowRule();
        configure(rule);
        Windows[name] = rule;
        return this;
    }
}
