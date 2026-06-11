using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Kun.Security.Authentication;

/// <summary>
/// Extension methods for registering Kun S2S JWT Bearer authentication on downstream services.
/// </summary>
public static class KunServiceAuthenticationExtensions
{
    /// <summary>
    /// Adds Kun S2S JWT Bearer authentication. Rejects HS256 tokens — only asymmetric
    /// algorithms (RS256, ES256) are accepted, consistent with the zero-trust policy.
    /// </summary>
    /// <param name="builder">The authentication builder.</param>
    /// <param name="configure">Delegate to configure <see cref="KunServiceAuthenticationOptions"/>.</param>
    /// <returns>The authentication builder for chaining.</returns>
    public static AuthenticationBuilder AddKunServiceAuthentication(
        this AuthenticationBuilder builder,
        Action<KunServiceAuthenticationOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        var opts = new KunServiceAuthenticationOptions();
        configure(opts);

        builder.AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, jwtOpts =>
        {
            jwtOpts.Authority = null; // manual validation — no discovery
            jwtOpts.RequireHttpsMetadata = false; // internal network; TLS handled at transport

            var validIssuers = BuildValidIssuers(opts);

            jwtOpts.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuers = validIssuers,
                ValidateAudience = true,
                ValidAudience = opts.Audience,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                // Reject all symmetric (HS*) algorithms — only RS256/ES256 allowed
                ValidAlgorithms = ["RS256", "ES256"],
                ClockSkew = TimeSpan.FromSeconds(30),
            };

            if (opts.StaticKeySet is not null)
            {
                jwtOpts.TokenValidationParameters.IssuerSigningKeys =
                    opts.StaticKeySet.GetSigningKeys();
            }
            else if (!string.IsNullOrWhiteSpace(opts.JwksUri))
            {
                // JwksUri points at a raw JWKS endpoint (per the option's contract),
                // not an OIDC discovery document — so parse it with
                // JwksConfigurationRetriever. OpenIdConnectConfigurationRetriever would
                // treat the response as discovery metadata and surface zero signing
                // keys (IDX10500). ConfigurationManager still caches the keys and
                // refreshes on an unknown kid.
                var retriever = new HttpDocumentRetriever { RequireHttps = false };
                jwtOpts.TokenValidationParameters.ConfigurationManager =
                    new ConfigurationManager<OpenIdConnectConfiguration>(
                        opts.JwksUri,
                        new JwksConfigurationRetriever(),
                        retriever);
            }
        });

        return builder;
    }

    private static List<string> BuildValidIssuers(KunServiceAuthenticationOptions opts)
    {
        var list = new List<string> { opts.Issuer };
        if (opts.AdditionalValidIssuers is not null)
            list.AddRange(opts.AdditionalValidIssuers);
        return list;
    }
}
