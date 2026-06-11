using System.Globalization;

namespace Kun.Identity.Tests.Infrastructure;

/// <summary>
/// Minimal <see cref="TimeProvider"/> double used by session / cookie
/// tests to advance time deterministically. Avoids pulling
/// <c>Microsoft.Extensions.Time.Testing</c> just for <c>GetUtcNow</c>
/// control.
/// </summary>
internal sealed class FakeTimeProvider : TimeProvider
{
    private DateTimeOffset _now = DateTimeOffset.Parse("2026-01-01T00:00:00Z", CultureInfo.InvariantCulture);

    public void SetUtcNow(DateTimeOffset value) => _now = value;

    public void Advance(TimeSpan delta) => _now = _now.Add(delta);

    public override DateTimeOffset GetUtcNow() => _now;
}
