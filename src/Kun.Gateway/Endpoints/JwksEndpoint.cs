using System.Text.Json.Serialization;
using Kun.Gateway.Configuration;
using Kun.Gateway.Jwks;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Kun.Gateway.Endpoints;

/// <summary>
/// Extension methods for mapping the public JWKS endpoint.
/// </summary>
public static class JwksEndpoint
{
    /// <summary>
    /// Maps <c>/.well-known/jwks.json</c> — returns the gateway's RSA public key as a JSON Web Key Set.
    /// The <c>kid</c> is derived from the SHA-256 hash of the RSA modulus for stable key identification.
    /// Response is cached by <see cref="JwksResponseBuilder"/> and only rebuilt when the
    /// underlying public-key file is modified.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <returns>The <see cref="IEndpointConventionBuilder"/> for the mapped route.</returns>
    public static IEndpointConventionBuilder MapKunJwks(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        return endpoints.MapGet("/.well-known/jwks.json",
            (IOptions<KunGatewayOptions> options, JwksResponseBuilder builder) =>
            {
                var keySet = builder.Build(options.Value.PublicKeyPath);
                return Results.Ok(keySet);
            });
    }
}

/// <summary>AOT-serializable JWKS response envelope.</summary>
[JsonSerializable(typeof(JwksResponse))]
internal sealed partial class JwksJsonContext : JsonSerializerContext { }

/// <summary>JSON Web Key Set response model.</summary>
public sealed class JwksResponse
{
    /// <summary>The list of public JSON Web Keys.</summary>
    [JsonPropertyName("keys")]
    public required IReadOnlyList<JwkDto> Keys { get; init; }
}

/// <summary>A single JSON Web Key entry in the JWKS response.</summary>
public sealed class JwkDto
{
    /// <summary>Initializes the DTO from a <see cref="JsonWebKey"/>.</summary>
    public JwkDto(JsonWebKey jwk)
    {
        Kty = jwk.Kty;
        Use = jwk.Use;
        Kid = jwk.KeyId;
        Alg = jwk.Alg;
        N = jwk.N;
        E = jwk.E;
    }

    /// <summary>Key type (e.g. <c>RSA</c>).</summary>
    [JsonPropertyName("kty")]
    public string Kty { get; }

    /// <summary>Key use (e.g. <c>sig</c>).</summary>
    [JsonPropertyName("use")]
    public string? Use { get; }

    /// <summary>Key ID.</summary>
    [JsonPropertyName("kid")]
    public string? Kid { get; }

    /// <summary>Algorithm (e.g. <c>RS256</c>).</summary>
    [JsonPropertyName("alg")]
    public string? Alg { get; }

    /// <summary>RSA modulus (Base64Url-encoded).</summary>
    [JsonPropertyName("n")]
    public string? N { get; }

    /// <summary>RSA public exponent (Base64Url-encoded).</summary>
    [JsonPropertyName("e")]
    public string? E { get; }
}
