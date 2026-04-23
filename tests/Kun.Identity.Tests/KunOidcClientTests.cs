using System.Net;
using System.Text;
using FluentAssertions;
using Kun.Identity.Configuration;
using Kun.Identity.Implementations;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Kun.Identity.Tests;

public sealed class KunOidcClientTests
{
    private const string Authority = "https://idp.test";

    [Fact]
    public async Task Discover_ParsesDocumentFromAuthority()
    {
        var handler = new StubHttpMessageHandler();
        handler.Register("GET", $"{Authority}/.well-known/openid-configuration",
            """
            {
              "issuer": "https://idp.test",
              "authorization_endpoint": "https://idp.test/auth",
              "token_endpoint": "https://idp.test/token",
              "userinfo_endpoint": "https://idp.test/me",
              "jwks_uri": "https://idp.test/jwks"
            }
            """);

        using var client = BuildClient(handler, clientSecret: "s3cr3t");
        var config = await client.DiscoverAsync(TestContext.Current.CancellationToken);

        config.Issuer.Should().Be("https://idp.test");
        config.AuthorizationEndpoint.Should().Be("https://idp.test/auth");
        config.TokenEndpoint.Should().Be("https://idp.test/token");
        config.UserInfoEndpoint.Should().Be("https://idp.test/me");
        config.JwksUri.Should().Be("https://idp.test/jwks");
    }

    [Fact]
    public async Task ExchangeCode_PostsAuthorizationCodeGrant()
    {
        var handler = new StubHttpMessageHandler();
        handler.Register("GET", $"{Authority}/.well-known/openid-configuration",
            """
            {
              "issuer": "https://idp.test",
              "authorization_endpoint": "https://idp.test/auth",
              "token_endpoint": "https://idp.test/token",
              "jwks_uri": "https://idp.test/jwks"
            }
            """);
        handler.Register("POST", "https://idp.test/token",
            """
            {
              "access_token": "access-xyz",
              "id_token": "id-xyz",
              "refresh_token": "refresh-xyz",
              "token_type": "Bearer",
              "expires_in": 3600
            }
            """);

        using var client = BuildClient(handler, clientSecret: "s3cr3t");

        var token = await client.ExchangeCodeAsync(
            "auth-code",
            "https://app.test/callback",
            TestContext.Current.CancellationToken);

        token.AccessToken.Should().Be("access-xyz");
        token.IdToken.Should().Be("id-xyz");
        token.RefreshToken.Should().Be("refresh-xyz");
        token.TokenType.Should().Be("Bearer");
        token.ExpiresIn.Should().Be(3600);

        handler.LastRequest.Should().NotBeNull();
        handler.LastRequest!.Headers.Authorization.Should().NotBeNull();
        handler.LastRequest.Headers.Authorization!.Scheme.Should().Be("Basic");

        var body = await handler.LastRequestBody!;
        body.Should().Contain("grant_type=authorization_code");
        body.Should().Contain("code=auth-code");
        body.Should().Contain("client_id=test-client");
    }

    private static KunOidcClient BuildClient(StubHttpMessageHandler handler, string clientSecret)
    {
        var services = new ServiceCollection();
        services.AddHttpClient(KunIdentityBuilder.OidcHttpClientName("test"))
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        var factory = services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>();
        return new KunOidcClient(
            new KunOidcClientOptions
            {
                Name = "test",
                Authority = Authority,
                ClientId = "test-client",
                ClientSecret = clientSecret,
            },
            factory);
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, string> _responses = new();

        public HttpRequestMessage? LastRequest { get; private set; }
        public Task<string>? LastRequestBody { get; private set; }

        public void Register(string method, string url, string responseBody)
            => _responses[$"{method} {url}"] = responseBody;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            if (request.Content is not null)
            {
                var snapshot = await request.Content.ReadAsStringAsync(cancellationToken);
                LastRequestBody = Task.FromResult(snapshot);
            }

            var key = $"{request.Method.Method} {request.RequestUri}";
            if (!_responses.TryGetValue(key, out var body))
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent($"no stub for {key}"),
                };
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
        }
    }
}
