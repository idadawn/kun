using System.Net;
using Kun.Identity.Abstractions.Session;
using Kun.Identity.AspNetCore;
using Kun.Identity.Configuration;
using Kun.Identity.Implementations.Session;
using Kun.IntegrationTests.Fixtures;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using Xunit;

namespace Kun.IntegrationTests.Identity;

public sealed class CookieBoundaryTests(RedisFixture redis) : IClassFixture<RedisFixture>
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RevokedUser_AnyCookieReturns401_AndOtherUserRemainsAuthenticated(bool distributed)
    {
        await using var connection = await ConnectionMultiplexer.ConnectAsync(redis.ConnectionString);
        ISessionRegistry store = distributed ? new RedisSessionStore(connection, TimeProvider.System, "http-test:" + Guid.NewGuid().ToString("N") + ":") : new InMemorySessionStore();
        await using var host = await CookieHost.StartAsync(store);
        foreach (var sid in new[] { "first-device", "second-device" })
        {
            await store.SetAsync(sid, Data("user-a"), TimeSpan.FromMinutes(2), TestContext.Current.CancellationToken);
            using var response = await host.SendAsync(HttpMethod.Get, "/protected", "kun.sid=" + sid);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        await store.SetAsync("other-device", Data("user-b"), TimeSpan.FromMinutes(2), TestContext.Current.CancellationToken);
        Assert.Equal(2, await store.RevokeUserAsync("user-a", TestContext.Current.CancellationToken));
        foreach (var sid in new[] { "first-device", "second-device" })
        {
            using var response = await host.SendAsync(HttpMethod.Get, "/protected", "kun.sid=" + sid);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
        using var other = await host.SendAsync(HttpMethod.Get, "/protected", "kun.sid=other-device");
        Assert.Equal(HttpStatusCode.OK, other.StatusCode);
    }

    [Fact]
    public async Task CookieWrite_RequiresAntiforgeryTokenBeforeHandler()
    {
        var store = new InMemorySessionStore();
        await store.SetAsync("signed-in", Data("user-a"), TimeSpan.FromMinutes(2), TestContext.Current.CancellationToken);
        await using var host = await CookieHost.StartAsync(store);
        using var denied = await host.SendAsync(HttpMethod.Post, "/write", "kun.sid=signed-in");
        Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode); Assert.Equal(0, host.Writes);
        using var tokenResponse = await host.SendAsync(HttpMethod.Get, "/csrf", "kun.sid=signed-in");
        Assert.Equal(HttpStatusCode.OK, tokenResponse.StatusCode);
        var token = await tokenResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var cookies = "kun.sid=signed-in; " + string.Join("; ", tokenResponse.Headers.GetValues("Set-Cookie").Select(value => value.Split(';')[0]).Where(value => !value.StartsWith("kun.sid=", StringComparison.Ordinal)));
        using var accepted = await host.SendAsync(HttpMethod.Post, "/write", cookies, token);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode); Assert.Equal(1, host.Writes);
    }

    private static SessionData Data(string user) => new(user, null, null, user, [], "access", null, DateTimeOffset.UtcNow.AddMinutes(2));

    private sealed class CookieHost(WebApplication app, HttpClient client) : IAsyncDisposable
    {
        private int _writes;
        internal int Writes => _writes;
        internal static async Task<CookieHost> StartAsync(ISessionRegistry store)
        {
            var builder = WebApplication.CreateBuilder(); builder.WebHost.UseUrls("http://127.0.0.1:0"); builder.Logging.ClearProviders();
            builder.Services.AddSingleton<ISessionStore>(store);
            builder.Services.AddKunIdentityAspNetCore();
            builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
            builder.Services.Configure<KunCookieAuthOptions>(options => options.Secure = KunCookieSecurePolicy.SameAsRequest);
            builder.Services.AddAuthorization();
            var app = builder.Build(); app.UseKunCookieAuth(); app.UseAuthentication(); app.UseAuthorization();
            var client = new HttpClient(new SocketsHttpHandler { UseProxy = false, UseCookies = false });
            var host = new CookieHost(app, client);
            app.MapGet("/protected", () => Results.Ok()).RequireAuthorization();
            app.MapPost("/write", () => { Interlocked.Increment(ref host._writes); return Results.Ok(); }).RequireAuthorization();
            app.MapGet("/csrf", (HttpContext context, IAntiforgery antiforgery) => Results.Text(antiforgery.GetAndStoreTokens(context).RequestToken!)).RequireAuthorization();
            await app.StartAsync(TestContext.Current.CancellationToken); client.BaseAddress = new Uri(app.Urls.Single());
            return host;
        }
        internal Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string cookie, string? csrf = null)
        {
            var request = new HttpRequestMessage(method, path); request.Headers.Add("Cookie", cookie);
            if (csrf is not null) request.Headers.Add("X-CSRF-TOKEN", csrf);
            return SendAndDisposeAsync(request);
        }
        private async Task<HttpResponseMessage> SendAndDisposeAsync(HttpRequestMessage request)
        { using (request) return await client.SendAsync(request, TestContext.Current.CancellationToken); }
        public async ValueTask DisposeAsync() { client.Dispose(); await app.DisposeAsync(); }
    }
}
