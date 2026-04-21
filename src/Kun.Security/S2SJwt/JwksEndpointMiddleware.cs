using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Kun.Security.S2SJwt;

/// <summary>
/// ASP.NET Core middleware that serves the JWKS at <c>/.well-known/jwks.json</c>.
/// Includes both the current and previous signing keys (for graceful rotation).
/// </summary>
public sealed partial class JwksEndpointMiddleware
{
    private readonly RequestDelegate _next;
    private readonly S2SJwtOptions _options;
    private readonly ILogger<JwksEndpointMiddleware> _logger;

    public JwksEndpointMiddleware(
        RequestDelegate next,
        IOptions<S2SJwtOptions> options,
        ILogger<JwksEndpointMiddleware> logger)
    {
        _next = next;
        _options = options.Value;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path == "/.well-known/jwks.json" &&
            string.Equals(context.Request.Method, "GET", StringComparison.OrdinalIgnoreCase))
        {
            var jwks = BuildJwks();
            var json = JsonSerializer.Serialize(jwks, JwksJsonSerializerContext.Default.JsonWebKeySet);

            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(json, context.RequestAborted);

            Log.JwksServed(_logger);
            return;
        }

        await _next(context);
    }

    private JsonWebKeySet BuildJwks()
    {
        var keySet = new JsonWebKeySet();

        var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(_options.SigningKeyPem);
        var securityKey = new ECDsaSecurityKey(ecdsa) { KeyId = _options.KeyId };
        var jwk = JsonWebKeyConverter.ConvertFromECDsaSecurityKey(securityKey);
        jwk.Use = "sig";
        jwk.Alg = "ES256";
        keySet.Keys.Add(jwk);

        if (!string.IsNullOrWhiteSpace(_options.PreviousSigningKeyPem))
        {
            var prevEcdsa = ECDsa.Create();
            prevEcdsa.ImportFromPem(_options.PreviousSigningKeyPem);
            var prevKey = new ECDsaSecurityKey(prevEcdsa) { KeyId = _options.PreviousKeyId ?? "previous" };
            var prevJwk = JsonWebKeyConverter.ConvertFromECDsaSecurityKey(prevKey);
            prevJwk.Use = "sig";
            prevJwk.Alg = "ES256";
            keySet.Keys.Add(prevJwk);
        }

        return keySet;
    }

    private static partial class Log
    {
        [LoggerMessage(LogLevel.Debug, "Served JWKS endpoint")]
        public static partial void JwksServed(ILogger logger);
    }
}

/// <summary>
/// Extension methods for registering the S2S JWKS endpoint middleware.
/// </summary>
public static class JwksEndpointExtensions
{
    /// <summary>
    /// Adds the JWKS endpoint middleware that serves <c>/.well-known/jwks.json</c>.
    /// </summary>
    public static IApplicationBuilder UseS2SJwksEndpoint(this IApplicationBuilder app)
    {
        return app.UseMiddleware<JwksEndpointMiddleware>();
    }
}
