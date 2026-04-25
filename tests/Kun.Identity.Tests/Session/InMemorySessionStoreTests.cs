using FluentAssertions;
using Kun.Identity.Abstractions.Session;
using Kun.Identity.Implementations.Session;
using Kun.Identity.Tests.Infrastructure;
using Xunit;

namespace Kun.Identity.Tests.Session;

public sealed class InMemorySessionStoreTests
{
    private static SessionData Sample(string userId = "user-1") => new(
        UserId: userId,
        TenantId: "tenant-a",
        Email: "alice@example.com",
        Name: "Alice",
        Roles: new[] { "admin", "editor" },
        AccessToken: "access",
        RefreshToken: "refresh",
        ExpiresAt: DateTimeOffset.UtcNow.AddMinutes(30));

    [Fact]
    public async Task SetThenGet_RoundTripsSessionData()
    {
        var clock = new FakeTimeProvider();
        var store = new InMemorySessionStore(clock);
        var ct = TestContext.Current.CancellationToken;

        await store.SetAsync("sid-1", Sample(), TimeSpan.FromMinutes(5), ct);
        var hit = await store.GetAsync("sid-1", ct);

        hit.Should().NotBeNull();
        hit!.UserId.Should().Be("user-1");
        hit.Roles.Should().Equal("admin", "editor");
    }

    [Fact]
    public async Task Get_ReturnsNullOnMiss()
    {
        var store = new InMemorySessionStore(new FakeTimeProvider());
        var ct = TestContext.Current.CancellationToken;

        (await store.GetAsync("absent", ct)).Should().BeNull();
    }

    [Fact]
    public async Task Get_PrunesExpiredEntry_AndReturnsNull()
    {
        var clock = new FakeTimeProvider();
        clock.SetUtcNow(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        var store = new InMemorySessionStore(clock);
        var ct = TestContext.Current.CancellationToken;

        await store.SetAsync("sid-exp", Sample(), TimeSpan.FromSeconds(10), ct);
        clock.Advance(TimeSpan.FromSeconds(11));

        (await store.GetAsync("sid-exp", ct)).Should().BeNull();
    }

    [Fact]
    public async Task Delete_RemovesEntry()
    {
        var store = new InMemorySessionStore(new FakeTimeProvider());
        var ct = TestContext.Current.CancellationToken;

        await store.SetAsync("sid-del", Sample(), TimeSpan.FromMinutes(5), ct);
        await store.DeleteAsync("sid-del", ct);

        (await store.GetAsync("sid-del", ct)).Should().BeNull();
    }

    [Fact]
    public async Task Set_RejectsNonPositiveTtl()
    {
        var store = new InMemorySessionStore(new FakeTimeProvider());
        var ct = TestContext.Current.CancellationToken;

        var act = () => store.SetAsync("sid", Sample(), TimeSpan.Zero, ct);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task Set_OverwritesPreviousEntry()
    {
        var store = new InMemorySessionStore(new FakeTimeProvider());
        var ct = TestContext.Current.CancellationToken;

        await store.SetAsync("sid", Sample(userId: "first"), TimeSpan.FromMinutes(5), ct);
        await store.SetAsync("sid", Sample(userId: "second"), TimeSpan.FromMinutes(5), ct);

        var hit = await store.GetAsync("sid", ct);
        hit!.UserId.Should().Be("second");
    }
}
