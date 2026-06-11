using FluentAssertions;
using Kun.Identity.Abstractions;
using Kun.Identity.AspNetCore;
using Kun.Identity.Configuration;
using Kun.Identity.Tests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Xunit;

namespace Kun.Identity.Tests.AspNetCore;

public sealed class ChallengeHelperTests
{
    [Fact]
    public void ChallengePayload_SerializeRoundTrip()
    {
        var original = new ChallengePayload(
            Client: "authentik",
            CodeVerifier: "verifier",
            State: "state123",
            Nonce: "nonce456",
            ReturnUrl: "/after-login?x=1");

        var wire = original.Serialize();
        var parsed = ChallengePayload.TryParse(wire, out var p);

        parsed.Should().BeTrue();
        p!.Client.Should().Be(original.Client);
        p.CodeVerifier.Should().Be(original.CodeVerifier);
        p.State.Should().Be(original.State);
        p.Nonce.Should().Be(original.Nonce);
        p.ReturnUrl.Should().Be(original.ReturnUrl);
    }

    [Fact]
    public void ChallengePayload_TryParse_RejectsMalformed()
    {
        ChallengePayload.TryParse("only|three|parts", out var p).Should().BeFalse();
        p.Should().BeNull();
    }

    [Fact]
    public async Task BuildLoginUrlAsync_WritesPkceParamsAndCookie()
    {
        var oidc = new FakeOidcClient("authentik");
        var options = Options.Create(new OptionsManagerFake()
            .With("authentik", new KunOidcClientOptions { Name = "authentik", ClientId = "cli-1", Authority = "https://idp" }));
        var optsMonitor = new StaticOptionsMonitor<KunOidcClientOptions>(
            byName: new Dictionary<string, KunOidcClientOptions>
            {
                ["authentik"] = new() { Name = "authentik", ClientId = "cli-1", Authority = "https://idp" },
            });
        var helper = new ChallengeHelper(new[] { (IOidcClient)oidc }, optsMonitor, new FakeTimeProvider());
        var ctx = new DefaultHttpContext();
        var ct = TestContext.Current.CancellationToken;

        var url = await helper.BuildLoginUrlAsync(ctx, "authentik", "https://app/cb", "/home", cancellationToken: ct);

        url.Should().StartWith("https://idp/authorize");
        url.Should().Contain("response_type=code");
        url.Should().Contain("client_id=cli-1");
        url.Should().Contain("code_challenge_method=S256");
        ctx.Response.Headers.SetCookie.Should().ContainSingle(
            c => c.ToString()!.Contains(ChallengeHelper.ChallengeCookieName));
    }

    [Fact]
    public async Task BuildLoginUrlAsync_ThrowsWhenClientUnknown()
    {
        var optsMonitor = new StaticOptionsMonitor<KunOidcClientOptions>(
            byName: new Dictionary<string, KunOidcClientOptions>());
        var helper = new ChallengeHelper(Array.Empty<IOidcClient>(), optsMonitor, new FakeTimeProvider());
        var ctx = new DefaultHttpContext();
        var ct = TestContext.Current.CancellationToken;

        var act = () => helper.BuildLoginUrlAsync(ctx, "missing", "https://app/cb", cancellationToken: ct);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .Where(e => e.Message.Contains("missing"));
    }

    [Fact]
    public void ConsumeChallengeCookie_DeletesCookieAndReturnsPayload()
    {
        var ctx = new DefaultHttpContext();
        var original = new ChallengePayload("authentik", "v", "s", "n", "/");
        ctx.Request.Headers.Cookie = $"{ChallengeHelper.ChallengeCookieName}={original.Serialize()}";

        var payload = ChallengeHelper.ConsumeChallengeCookie(ctx);

        payload.Should().NotBeNull();
        payload!.Client.Should().Be("authentik");
        ctx.Response.Headers.SetCookie.Should().ContainSingle(
            c => c.ToString()!.Contains(ChallengeHelper.ChallengeCookieName) &&
                 c.ToString()!.Contains("expires=Thu, 01 Jan 1970"));
    }

    private sealed class FakeOidcClient(string name) : IOidcClient
    {
        public string Name { get; } = name;
        public Task<OidcConfiguration> DiscoverAsync(CancellationToken ct = default)
            => Task.FromResult(new OidcConfiguration(
                Issuer: "https://idp",
                AuthorizationEndpoint: "https://idp/authorize",
                TokenEndpoint: "https://idp/token",
                UserInfoEndpoint: null,
                JwksUri: "https://idp/jwks"));
        public Task<OidcTokenResponse> ExchangeCodeAsync(string code, string redirectUri, CancellationToken ct = default)
            => throw new NotImplementedException();
    }

    private sealed class OptionsManagerFake
    {
        private readonly Dictionary<string, KunOidcClientOptions> _by = new(StringComparer.Ordinal);
        public OptionsManagerFake With(string name, KunOidcClientOptions o) { _by[name] = o; return this; }
        public KunOidcClientOptions Value => _by.Values.First();
    }

    private sealed class StaticOptionsMonitor<T> : IOptionsMonitor<T> where T : class, new()
    {
        private readonly Dictionary<string, T> _by;
        public StaticOptionsMonitor(Dictionary<string, T> byName) => _by = byName;
        public T CurrentValue => _by.Values.FirstOrDefault() ?? new T();
        public T Get(string? name) => _by.TryGetValue(name ?? string.Empty, out var v) ? v : new T();
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
