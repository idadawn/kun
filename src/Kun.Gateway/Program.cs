using Kun.Gateway.DependencyInjection;
using Kun.Gateway.Endpoints;
using Kun.Gateway.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// ── Gateway core (S2sTokenIssuer, YARP with S2sAuthTransform + Routes/Clusters) ─
builder.Services.AddKunGateway(
    opts => builder.Configuration.GetSection("KunGateway").Bind(opts),
    builder.Configuration.GetSection("ReverseProxy"));

// ── Rate limiting (tenant-partitioned, 100 req/min) ───────────────────────────
builder.Services.AddKunRateLimiter();

// ── External JWT validation (IdP-issued; gateway validates before proxying) ───
// Note: AddKunServiceAuthentication is for downstream services validating S2S tokens.
// The gateway validates external IdP tokens directly with JwtBearer.
var gatewaySection = builder.Configuration.GetSection("KunGateway");
var externalIssuer = gatewaySection["ExternalIssuer"] ?? string.Empty;
var externalAudience = gatewaySection["ExternalAudience"] ?? string.Empty;
var externalJwksUri = gatewaySection["ExternalJwksUri"] ?? string.Empty;

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MetadataAddress = externalJwksUri;
        options.RequireHttpsMetadata = false; // may be HTTP in dev; enforce in prod via config
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = externalIssuer,
            ValidateAudience = true,
            ValidAudience = externalAudience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidAlgorithms = ["RS256", "ES256"],
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

// gRPC-Web: translates grpc-web (HTTP/1.1) requests to gRPC (HTTP/2) before YARP forwards them.
// DefaultEnabled=true means all routes support grpc-web without per-route opt-in.
app.UseGrpcWeb(new GrpcWebOptions { DefaultEnabled = true });

// Expose the gateway's public key for downstream S2S JWT validation
app.MapKunJwks();

// YARP reverse proxy — routes and clusters configured via appsettings.json
app.MapReverseProxy();

app.Run();
