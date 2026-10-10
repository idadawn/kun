using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kun.Identity.AspNetCore;
using Kun.Identity.Configuration;
using Kun.Identity.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Kun.IntegrationTests.Identity;

/// <summary>Real HTTP/RSA protocol fixture for the RP boundary; this is not the still-unimplemented Qian identity server acceptance.</summary>
public sealed class OidcBrowserFlowTests
{
    [Fact]
    public async Task Login_PkceNonceAndDiHelpers_CreateSession_ThenSignedBackchannelRevokesIt()
    {
        await using var flow = await FlowHost.StartAsync();
        using var login = await flow.LoginAsync();
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        var cookie = login.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith("kun.sid=", StringComparison.Ordinal)).Split(';')[0];
        using var accepted = await flow.GetAsync("/protected", cookie);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        using var wrongPurpose = await flow.LogoutAsync(flow.Token("id-nonce", logout: false));
        Assert.Equal(HttpStatusCode.BadRequest, wrongPurpose.StatusCode);
        using var stillValid = await flow.GetAsync("/protected", cookie);
        Assert.Equal(HttpStatusCode.OK, stillValid.StatusCode);
        using var logout = await flow.LogoutAsync(flow.Token(nonce: null, logout: true));
        Assert.Equal(HttpStatusCode.OK, logout.StatusCode);
        using var revoked = await flow.GetAsync("/protected", cookie);
        Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);
        Assert.Equal(1, flow.Exchanges);
    }

    [Fact]
    public async Task SignedWrongNonce_NeverCreatesLocalSession()
    {
        await using var flow = await FlowHost.StartAsync(); flow.TamperNonce = true;
        using var response = await flow.LoginAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.DoesNotContain(response.Headers.GetValues("Set-Cookie"), value => value.StartsWith("kun.sid=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WrongState_IsRejectedBeforeTokenExchange()
    {
        await using var flow = await FlowHost.StartAsync();
        using var response = await flow.LoginAsync(wrongState: true);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode); Assert.Equal(0, flow.Exchanges);
    }

    private sealed class FlowHost : IAsyncDisposable
    {
        private readonly RSA _key = RSA.Create(2048);
        private readonly ConcurrentDictionary<string, Code> _codes = new();
        private readonly HttpClient _client = new(new SocketsHttpHandler { UseProxy = false, UseCookies = false, AllowAutoRedirect = false });
        private WebApplication _provider = null!;
        private WebApplication _rp = null!;
        private int _exchanges;
        internal bool TamperNonce { get; set; }
        internal int Exchanges => _exchanges;
        private string Authority => _provider.Urls.Single();
        private string AppUrl => _rp.Urls.Single();

        internal static async Task<FlowHost> StartAsync()
        {
            var flow = new FlowHost();
            var provider = WebApplication.CreateBuilder(); provider.WebHost.UseUrls("http://127.0.0.1:0"); provider.Logging.ClearProviders();
            flow._provider = provider.Build();
            flow._provider.MapGet("/.well-known/openid-configuration", () => Results.Json(new { issuer = flow.Authority, authorization_endpoint = flow.Authority + "/authorize", token_endpoint = flow.Authority + "/token", jwks_uri = flow.Authority + "/jwks" }));
            var key = flow._key.ExportParameters(false);
            var jwks = JsonSerializer.Serialize(new { keys = new[] { new { kty = "RSA", kid = "provider-key", alg = "RS256", n = Base64UrlEncoder.Encode(key.Modulus!), e = Base64UrlEncoder.Encode(key.Exponent!) } } });
            flow._provider.MapGet("/jwks", () => Results.Text(jwks, "application/json"));
            flow._provider.MapGet("/authorize", (HttpContext context) =>
            {
                var query = context.Request.Query;
                if (query["client_id"] != "probe-client" || query["code_challenge_method"] != "S256") return Results.BadRequest();
                var code = Guid.NewGuid().ToString("N");
                flow._codes[code] = new Code(query["code_challenge"].ToString(), query["nonce"].ToString(), query["redirect_uri"].ToString());
                return Results.Redirect(QueryHelpers.AddQueryString(query["redirect_uri"].ToString(), new Dictionary<string, string?> { ["code"] = code, ["state"] = query["state"].ToString() }));
            });
            flow._provider.MapPost("/token", (RequestDelegate)(async context =>
            {
                Interlocked.Increment(ref flow._exchanges);
                var form = await context.Request.ReadFormAsync(context.RequestAborted);
                var challenge = Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(form["code_verifier"].ToString())));
                if (form["grant_type"] != "authorization_code" || !flow._codes.TryRemove(form["code"].ToString(), out var code) ||
                    challenge != code.Challenge || form["redirect_uri"] != code.RedirectUri ||
                    context.Request.Headers.Authorization != "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("probe-client:probe-secret")))
                { context.Response.StatusCode = StatusCodes.Status400BadRequest; return; }
                await context.Response.WriteAsJsonAsync(new { access_token = "test-access", id_token = flow.Token(flow.TamperNonce ? "wrong-nonce" : code.Nonce, logout: false), refresh_token = "test-refresh", token_type = "Bearer", expires_in = 120 }, context.RequestAborted);
            }));
            await flow._provider.StartAsync(TestContext.Current.CancellationToken);
            var rp = WebApplication.CreateBuilder(); rp.WebHost.UseUrls("http://127.0.0.1:0"); rp.Logging.ClearProviders();
            rp.Services.AddKunIdentity(builder =>
            {
                builder.AddOidcClient("probe", options => { options.Authority = flow.Authority; options.ClientId = "probe-client"; options.ClientSecret = "probe-secret"; options.RequireHttpsMetadata = false; });
                builder.AddCookieSession(options => { options.UseInMemoryStore(); options.Secure = KunCookieSecurePolicy.SameAsRequest; });
            });
            rp.Services.ConfigureHttpClientDefaults(builder => builder.ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { UseProxy = false }));
            rp.Services.AddKunIdentityAspNetCore(); rp.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
            rp.Services.AddAuthorization();
            flow._rp = rp.Build(); flow._rp.UseKunCookieAuth(); flow._rp.UseAuthentication(); flow._rp.UseAuthorization();
            flow._rp.MapGet("/login", async (HttpContext context, ChallengeHelper challenge) => Results.Redirect(await challenge.BuildLoginUrlAsync(context, "probe", flow.AppUrl + "/callback", cancellationToken: context.RequestAborted)));
            flow._rp.MapGet("/callback", (RequestDelegate)(async context =>
            {
                try { context.Response.Redirect(await context.RequestServices.GetRequiredService<OidcLoginService>().CompleteAsync(context, context.Request.Query["code"].ToString(), context.Request.Query["state"].ToString(), context.RequestAborted)); }
                catch (Exception error) when (error is SecurityTokenException or InvalidOperationException or ArgumentException) { context.Response.StatusCode = StatusCodes.Status401Unauthorized; }
            }));
            flow._rp.MapGet("/protected", () => Results.Ok()).RequireAuthorization();
            flow._rp.MapKunBackChannelLogout("/backchannel", "probe");
            await flow._rp.StartAsync(TestContext.Current.CancellationToken);
            return flow;
        }

        internal async Task<HttpResponseMessage> LoginAsync(bool wrongState = false)
        {
            using var login = await _client.GetAsync(AppUrl + "/login", TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
            var cookie = login.Headers.GetValues("Set-Cookie").Single().Split(';')[0];
            using var authorize = await _client.GetAsync(login.Headers.Location, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Redirect, authorize.StatusCode);
            var callback = authorize.Headers.Location!;
            if (wrongState) callback = new Uri(QueryHelpers.AddQueryString(AppUrl + "/callback", new Dictionary<string, string?> { ["code"] = QueryHelpers.ParseQuery(callback.Query)["code"].ToString(), ["state"] = "wrong-state" }));
            using var request = new HttpRequestMessage(HttpMethod.Get, callback); request.Headers.Add("Cookie", cookie);
            return await _client.SendAsync(request, TestContext.Current.CancellationToken);
        }
        internal async Task<HttpResponseMessage> GetAsync(string path, string cookie)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, AppUrl + path); request.Headers.Add("Cookie", cookie);
            return await _client.SendAsync(request, TestContext.Current.CancellationToken);
        }
        internal async Task<HttpResponseMessage> LogoutAsync(string token)
        {
            using var body = new FormUrlEncodedContent(new Dictionary<string, string> { ["logout_token"] = token });
            return await _client.PostAsync(AppUrl + "/backchannel", body, TestContext.Current.CancellationToken);
        }
        internal string Token(string? nonce, bool logout)
        {
            var now = DateTime.UtcNow;
            var claims = new Dictionary<string, object> { ["sub"] = "user-a", ["sid"] = "provider-session", ["jti"] = Guid.NewGuid().ToString("N") };
            if (nonce is not null) claims["nonce"] = nonce;
            if (logout) claims["events"] = new Dictionary<string, object> { ["http://schemas.openid.net/event/backchannel-logout"] = new Dictionary<string, object>() };
            return new JwtSecurityTokenHandler().CreateEncodedJwt(new SecurityTokenDescriptor
            { Issuer = Authority, Audience = "probe-client", IssuedAt = now, NotBefore = now, Expires = now.AddMinutes(2), Claims = claims, SigningCredentials = new SigningCredentials(new RsaSecurityKey(_key) { KeyId = "provider-key" }, SecurityAlgorithms.RsaSha256) });
        }
        public async ValueTask DisposeAsync() { _client.Dispose(); if (_rp is not null) await _rp.DisposeAsync(); if (_provider is not null) await _provider.DisposeAsync(); _key.Dispose(); }
        private sealed record Code(string Challenge, string Nonce, string RedirectUri);
    }
}
