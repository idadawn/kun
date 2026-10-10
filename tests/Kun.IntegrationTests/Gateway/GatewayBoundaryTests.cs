using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Threading.RateLimiting;
using FluentAssertions;
using Json.Schema;
using Kun.Gateway;
using Kun.Gateway.RateLimiting.Resolvers;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Kun.IntegrationTests.Gateway;

public sealed class GatewayBoundaryTests
{
    [Fact]
    public async Task Gateway_DefaultRoute_RejectsAnonymousAndAllowsOnlyExplicitPublicRoute()
    {
        await using var host = await BoundaryHost.StartAsync();
        using var denied = await host.Client.GetAsync("/protected", TestContext.Current.CancellationToken);
        denied.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        host.BackendCalls.Should().Be(0);
        using var allowed = await host.Client.GetAsync("/public", TestContext.Current.CancellationToken);
        allowed.StatusCode.Should().Be(HttpStatusCode.OK);
        host.BackendCalls.Should().Be(1);
    }

    [Fact]
    public async Task Gateway_RoutePolicyAndDefaultPolicy_Return429WithRetryAfter()
    {
        await using var host = await BoundaryHost.StartAsync();
        host.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", host.Token());
        for (var i = 0; i < 2; i++)
        {
            using var response = await host.Client.GetAsync("/limited", TestContext.Current.CancellationToken);
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }
        using var limited = await host.Client.GetAsync("/limited", TestContext.Current.CancellationToken);
        limited.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        limited.Headers.RetryAfter.Should().NotBeNull();
        for (var i = 0; i < 100; i++)
        {
            using var response = await host.Client.GetAsync("/default", TestContext.Current.CancellationToken);
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }
        using var overflow = await host.Client.GetAsync("/default", TestContext.Current.CancellationToken);
        overflow.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        overflow.Headers.RetryAfter.Should().NotBeNull();
    }

    [Theory]
    [InlineData("none", false)]
    [InlineData("other", false)]
    [InlineData("address", true)]
    [InlineData("network", true)]
    public async Task Gateway_ForwardedFor_ChangesPartitionOnlyForTrustedPeer(string trust, bool trusted)
    {
        await using var host = await BoundaryHost.StartAsync(trust);
        for (var i = 0; i < 3; i++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/ip");
            request.Headers.Add("X-Forwarded-For", "203.0.113." + (10 + i).ToString(CultureInfo.InvariantCulture));
            request.Headers.Add("X-Forwarded-Proto", "https");
            using var response = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);
            response.StatusCode.Should().Be(i < 2 || trusted ? HttpStatusCode.OK : HttpStatusCode.TooManyRequests);
        }
    }

    [Fact]
    public async Task Gateway_AnonymousRoute_DoesNotForwardUnvalidatedAuthorization()
    {
        await using var host = await BoundaryHost.StartAsync();
        host.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "invalid-token");
        using var response = await host.Client.GetAsync("/public", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().BeEmpty();
    }

    [Fact]
    public async Task Gateway_KeyRotation_UsesNewKidKeepsOldPublicKeyThenRetiresItWithoutRestart()
    {
        await using var host = await BoundaryHost.StartAsync();
        host.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", host.Token());
        var oldToken = (await host.Client.GetStringAsync("/public", TestContext.Current.CancellationToken))["Bearer ".Length..];
        var decoder = new JwtSecurityTokenHandler();
        var oldKid = decoder.ReadJwtToken(oldToken).Header.Kid;
        using var next = RSA.Create(2048);
        host.Rotate(next.ExportRSAPrivateKeyPem());
        var newToken = (await host.Client.GetStringAsync("/public", TestContext.Current.CancellationToken))["Bearer ".Length..];
        decoder.ReadJwtToken(newToken).Header.Kid.Should().NotBe(oldKid);
        var overlapping = await host.Client.GetStringAsync("/.well-known/jwks.json", TestContext.Current.CancellationToken);
        var keys = new JsonWebKeySet(overlapping);
        keys.Keys.Should().HaveCount(2);
        keys.Keys.Should().OnlyContain(key => key.D == null && key.P == null && key.Q == null);
        Validate(oldToken, keys).Identity!.IsAuthenticated.Should().BeTrue();
        Validate(newToken, keys).Identity!.IsAuthenticated.Should().BeTrue();
        host.Clock.Advance(TimeSpan.FromSeconds(31));
        var retired = new JsonWebKeySet(await host.Client.GetStringAsync("/.well-known/jwks.json", TestContext.Current.CancellationToken));
        retired.Keys.Should().ContainSingle().Which.Kid.Should().NotBe(oldKid);
        var reject = () => Validate(oldToken, retired);
        reject.Should().Throw<SecurityTokenException>();
        Validate(newToken, retired).Identity!.IsAuthenticated.Should().BeTrue();
    }

    [Fact]
    public async Task Gateway_InvalidReplacement_PreservesLastGoodKeyAndHealthRoutesStayAnonymous()
    {
        await using var host = await BoundaryHost.StartAsync();
        host.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", host.Token());
        var before = (await host.Client.GetStringAsync("/public", TestContext.Current.CancellationToken))["Bearer ".Length..];
        host.Rotate("incomplete-key");
        var after = (await host.Client.GetStringAsync("/public", TestContext.Current.CancellationToken))["Bearer ".Length..];
        var decoder = new JwtSecurityTokenHandler();
        decoder.ReadJwtToken(before).Header.Kid.Should().Be(decoder.ReadJwtToken(after).Header.Kid);
        host.Client.DefaultRequestHeaders.Authorization = null;
        foreach (var route in new[] { "/healthz", "/readyz", "/.well-known/jwks.json" })
        {
            using var response = await host.Client.GetAsync(route, TestContext.Current.CancellationToken);
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    [Theory]
    [InlineData("user", false, 0)]
    [InlineData("user", false, 1)]
    [InlineData("user", true, 2)]
    [InlineData("service", false, 0)]
    [InlineData("service", true, 1)]
    public async Task Gateway_S2sPayload_MatchesFrozen202012Contract(string kind, bool platform, int roles)
    {
        // Frozen source: idadawn/qian 5eaed840, contracts/v0/s2s-claims.schema.json.
        await using var host = await BoundaryHost.StartAsync();
        host.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", host.Token(kind, platform, roles));
        var token = (await host.Client.GetStringAsync("/public", TestContext.Current.CancellationToken))["Bearer ".Length..];
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        var payload = JsonNode.Parse(jwt.Payload.SerializeToJson())!;
        var schema = JsonSchema.FromFile(Path.Combine(AppContext.BaseDirectory, "Fixtures", "s2s-claims.schema.json"));
        schema.Evaluate(payload).IsValid.Should().BeTrue(payload.ToJsonString());
        payload["aud"].Should().BeOfType<JsonArray>().Which.Should().HaveCount(1);
        payload["roles"].Should().BeOfType<JsonArray>().Which.Should().HaveCount(roles);
        (payload["exp"]!.GetValue<long>() - payload["iat"]!.GetValue<long>()).Should().BeInRange(1, 300);
        if (kind == "service") payload["session_id"]!.GetValue<string>().Should().Be(payload["jti"]!.GetValue<string>());
        payload.AsObject().Should().NotContainKey("email").And.NotContainKey("trace_id").And.NotContainKey("permission");
    }

    private static ClaimsPrincipal Validate(string token, JsonWebKeySet keys) => new JwtSecurityTokenHandler().ValidateToken(token, new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidIssuer = "kun-gateway",
        ValidateAudience = true,
        ValidAudience = "kun-internal",
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        IssuerSigningKeys = keys.GetSigningKeys(),
        ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
        ClockSkew = TimeSpan.Zero
    }, out _);

    private sealed class Clock : TimeProvider
    {
        private long _ticks = DateTimeOffset.UtcNow.UtcTicks;
        public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref _ticks), TimeSpan.Zero);
        internal void Advance(TimeSpan duration) => Interlocked.Add(ref _ticks, duration.Ticks);
    }

    private sealed class BoundaryHost : IAsyncDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "kun-gateway-boundary-" + Guid.NewGuid().ToString("N"));
        private readonly RSA _externalKey = RSA.Create(2048);
        private WebApplication _backend = null!;
        private WebApplicationFactory<S2sTokenIssuer> _factory = null!;
        private int _backendCalls;
        internal Clock Clock { get; } = new();
        internal void Rotate(string pem)
        {
            var next = Path.Combine(_directory, "replacement.pem");
            File.WriteAllText(next, pem);
            File.Move(next, Path.Combine(_directory, "private.pem"), overwrite: true);
        }
        internal HttpClient Client { get; private set; } = null!;
        internal int BackendCalls => _backendCalls;
        internal static async Task<BoundaryHost> StartAsync(string trust = "none")
        {
            var fixture = new BoundaryHost(); Directory.CreateDirectory(fixture._directory);
            using var internalKey = RSA.Create(2048);
            var privateFile = Path.Combine(fixture._directory, "private.pem"); var publicFile = Path.Combine(fixture._directory, "public.pem");
            File.WriteAllText(privateFile, internalKey.ExportRSAPrivateKeyPem()); File.WriteAllText(publicFile, internalKey.ExportRSAPublicKeyPem());
            var builder = WebApplication.CreateBuilder(); builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
            fixture._backend = builder.Build();
            fixture._backend.MapGet("/{**path}", (HttpContext http) => { Interlocked.Increment(ref fixture._backendCalls); return http.Request.Headers.Authorization.ToString(); });
            await fixture._backend.StartAsync(TestContext.Current.CancellationToken);
            var values = new Dictionary<string, string?>
            {
                ["KunObservability:TracingEnabled"] = "false",
                ["KunObservability:MetricsEnabled"] = "false",
                ["KunObservability:LoggingEnabled"] = "false",
                ["KunGateway:ExternalIssuer"] = "https://idp.test",
                ["KunGateway:ExternalAudience"] = "test-api",
                ["KunGateway:ExternalJwksUri"] = "https://idp.test/.well-known/openid-configuration",
                ["KunGateway:KeyOverlapPeriod"] = "00:00:30",
                ["KunGateway:KeyReloadInterval"] = "00:00:00",
                ["KunGateway:SigningKeyPath"] = privateFile,
                ["KunGateway:PublicKeyPath"] = publicFile,
                ["ReverseProxy:Clusters:boundary:Destinations:one:Address"] = fixture._backend.Urls.Single()
            };
            if (trust is "address" or "other") values["KunGateway:TrustedProxyAddresses:0"] = trust == "address" ? "127.0.0.1" : "10.0.0.2";
            if (trust == "network") values["KunGateway:TrustedProxyNetworks:0"] = "127.0.0.0/8";
            foreach (var route in new[] { "protected", "public", "limited", "default", "ip" })
            {
                values["ReverseProxy:Routes:" + route + ":ClusterId"] = "boundary";
                values["ReverseProxy:Routes:" + route + ":Match:Path"] = "/" + route;
            }
            values["ReverseProxy:Routes:public:AuthorizationPolicy"] = "Anonymous";
            values["ReverseProxy:Routes:ip:AuthorizationPolicy"] = "Anonymous";
            values["ReverseProxy:Routes:ip:RateLimiterPolicy"] = "probe-ip";
            values["ReverseProxy:Routes:limited:RateLimiterPolicy"] = "probe-low";
            fixture._factory = new WebApplicationFactory<S2sTokenIssuer>().WithWebHostBuilder(web =>
            {
                web.UseEnvironment(Environments.Production).ConfigureLogging(logging => logging.ClearProviders());
                web.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(values));
                web.ConfigureTestServices(services =>
                {
                    services.AddSingleton<TimeProvider>(fixture.Clock);
                    services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                    {
                        options.Configuration = new OpenIdConnectConfiguration { Issuer = "https://idp.test" };
                        options.Configuration.SigningKeys.Add(new RsaSecurityKey(fixture._externalKey));
                        options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(options.Configuration);
                        options.MetadataAddress = string.Empty; options.MapInboundClaims = false;
                        options.TokenValidationParameters.ValidIssuer = "https://idp.test"; options.TokenValidationParameters.ValidAudience = "test-api";
                    });
                    services.AddRateLimiter(options =>
                    {
                        options.AddFixedWindowLimiter("probe-low", limiter => { limiter.PermitLimit = 2; limiter.Window = TimeSpan.FromMinutes(1); limiter.QueueLimit = 0; });
                        options.AddPolicy("probe-ip", http => RateLimitPartition.GetFixedWindowLimiter(new IpRateLimitPartitionResolver().ResolvePartitionKey(http), _ => new FixedWindowRateLimiterOptions { PermitLimit = 2, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
                    });
                });
            });
            fixture._factory.UseKestrel(0); fixture._factory.StartServer();
            var address = fixture._factory.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            fixture.Client = new HttpClient(new SocketsHttpHandler { UseProxy = false }) { BaseAddress = new UriBuilder(address) { Host = "127.0.0.1" }.Uri };
            return fixture;
        }
        internal string Token(string kind = "user", bool platform = false, int roleCount = 0)
        {
            var now = DateTime.UtcNow;
            var claims = new Dictionary<string, object>
            {
                ["sub"] = "probe-user",
                ["principal_kind"] = kind,
                ["tenant_id"] = platform ? null! : "00000000-0000-4000-8000-000000000046",
                ["roles"] = Enumerable.Range(0, roleCount).Select(index => "role-" + index).ToArray(),
                ["scope"] = "catalog.read",
                ["email"] = "private@example.invalid",
                ["trace_id"] = "not-a-payload-field",
                ["permission"] = "not-forwarded"
            };
            if (kind == "user")
            {
                claims["session_id"] = "00000000-0000-4000-8000-000000000100"; claims["identity_revision"] = 0;
                if (!platform) claims["membership_id"] = "00000000-0000-4000-8000-000000000200";
            }
            return new JwtSecurityTokenHandler().CreateEncodedJwt(new SecurityTokenDescriptor
            {
                Issuer = "https://idp.test",
                Audience = "test-api",
                IssuedAt = now,
                NotBefore = now,
                Expires = now.AddMinutes(2),
                Claims = claims,
                SigningCredentials = new SigningCredentials(new RsaSecurityKey(_externalKey), SecurityAlgorithms.RsaSha256)
            });
        }
        public async ValueTask DisposeAsync()
        {
            Client.Dispose(); await _factory.DisposeAsync(); await _backend.DisposeAsync(); _externalKey.Dispose(); Directory.Delete(_directory, true);
        }
    }
}
