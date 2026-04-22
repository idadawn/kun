using System.Text.Json.Serialization;

namespace Kun.Identity.Implementations;

/// <summary>
/// Source-generated JSON contract for the OIDC discovery + token
/// documents. Explicit context keeps <see cref="KunOidcClient"/>
/// reflection-free and AOT-analyzer clean.
/// </summary>
[JsonSerializable(typeof(OidcDiscoveryDocument))]
[JsonSerializable(typeof(OidcTokenDocument))]
internal sealed partial class OidcJsonContext : JsonSerializerContext
{
}

internal sealed class OidcDiscoveryDocument
{
    [JsonPropertyName("issuer")] public string Issuer { get; set; } = string.Empty;
    [JsonPropertyName("authorization_endpoint")] public string AuthorizationEndpoint { get; set; } = string.Empty;
    [JsonPropertyName("token_endpoint")] public string TokenEndpoint { get; set; } = string.Empty;
    [JsonPropertyName("userinfo_endpoint")] public string? UserInfoEndpoint { get; set; }
    [JsonPropertyName("jwks_uri")] public string JwksUri { get; set; } = string.Empty;
}

internal sealed class OidcTokenDocument
{
    [JsonPropertyName("access_token")] public string AccessToken { get; set; } = string.Empty;
    [JsonPropertyName("id_token")] public string? IdToken { get; set; }
    [JsonPropertyName("refresh_token")] public string? RefreshToken { get; set; }
    [JsonPropertyName("token_type")] public string? TokenType { get; set; }
    [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
}
