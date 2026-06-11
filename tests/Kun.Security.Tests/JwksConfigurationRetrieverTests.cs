using System.Security.Cryptography;
using FluentAssertions;
using Kun.Security.Authentication;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Kun.Security.Tests;

/// <summary>
/// Tests for <see cref="JwksConfigurationRetriever"/> — verifies a raw JWKS
/// document is parsed into signing keys (the gap that previously caused
/// <c>JwksUri</c> endpoints to surface zero keys / IDX10500).
/// </summary>
public sealed class JwksConfigurationRetrieverTests
{
    private sealed class StubDocumentRetriever(string document) : IDocumentRetriever
    {
        public Task<string> GetDocumentAsync(string address, CancellationToken cancel) =>
            Task.FromResult(document);
    }

    private static string BuildRsaJwksJson(string kid)
    {
        using var rsa = RSA.Create(2048);
        var jwk = JsonWebKeyConverter.ConvertFromRSASecurityKey(
            new RsaSecurityKey(rsa) { KeyId = kid });
        return $$"""
            {"keys":[{"kty":"RSA","kid":"{{kid}}","use":"sig","alg":"RS256","n":"{{jwk.N}}","e":"{{jwk.E}}"}]}
            """;
    }

    [Fact]
    public async Task GetConfigurationAsync_ParsesRawJwks_ExposesSigningKeys()
    {
        var json = BuildRsaJwksJson("key-1");
        var retriever = new JwksConfigurationRetriever();

        var config = await retriever.GetConfigurationAsync(
            "https://edge.internal/.well-known/jwks.json",
            new StubDocumentRetriever(json),
            TestContext.Current.CancellationToken);

        config.SigningKeys.Should().ContainSingle();
        config.SigningKeys.Single().KeyId.Should().Be("key-1");
        config.JsonWebKeySet.Should().NotBeNull();
    }

    [Fact]
    public async Task GetConfigurationAsync_NullRetriever_Throws()
    {
        var retriever = new JwksConfigurationRetriever();

        var act = async () => await retriever.GetConfigurationAsync(
            "https://edge.internal/.well-known/jwks.json",
            null!,
            TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
