namespace Kun.Identity.Configuration;

/// <summary>Per-provider configuration for <c>KunOidcClient</c>.</summary>
public sealed class KunOidcClientOptions
{
    /// <summary>Logical name for this client; used as the DI key.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Provider authority — used to resolve the OIDC discovery
    /// document at <c>{Authority}/.well-known/openid-configuration</c>.
    /// The trailing slash is normalised by the client.
    /// </summary>
    public string Authority { get; set; } = string.Empty;

    /// <summary>OIDC client id registered with the provider.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>OIDC client secret. Leave empty when the provider uses PKCE / public clients.</summary>
    public string ClientSecret { get; set; } = string.Empty;
    /// <summary>Maximum metadata cache age. Defaults to five minutes; zero forces a refresh for every operation.</summary>
    public TimeSpan DiscoveryRefreshInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Require HTTPS for provider metadata and advertised endpoints. Disable only for local test providers.</summary>
    public bool RequireHttpsMetadata { get; set; } = true;
}
