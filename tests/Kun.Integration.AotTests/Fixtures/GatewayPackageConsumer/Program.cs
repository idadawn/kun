using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Kun.Gateway.Configuration;
using Kun.Gateway.DependencyInjection;
using Kun.Gateway.Endpoints;
using Kun.Gateway.Jwks;
using Microsoft.Extensions.Options;

// 此宿主会被复制到仓库之外，只消费普通打包的 Kun.Gateway；不能改成 ProjectReference。
var directory = Path.Combine(Directory.GetCurrentDirectory(), $"kun-gateway-native-keys-{Guid.NewGuid():N}");
Directory.CreateDirectory(directory);
try
{
    var privateKey = Path.Combine(directory, "private.pem");
    var publicKey = Path.Combine(directory, "public.pem");
    using var initialKey = RSA.Create(2048);
    File.WriteAllText(privateKey, initialKey.ExportRSAPrivateKeyPem());
    File.WriteAllText(publicKey, initialKey.ExportSubjectPublicKeyInfoPem());

    var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
    builder.Logging.ClearProviders();
    builder.WebHost.UseUrls("http://127.0.0.1:0");
    var clock = new ProbeClock();
    builder.Services.AddSingleton<TimeProvider>(clock);
    builder.Services.AddAuthentication();
    builder.Services.AddKunGateway(options =>
    {
        options.ExternalIssuer = "https://unused.example";
        options.ExternalAudience = "unused";
        options.ExternalJwksUri = "https://unused.example/.well-known/jwks.json";
        options.SigningKeyPath = privateKey;
        options.PublicKeyPath = publicKey;
        options.ExposeInternalJwks = false;
        options.KeyReloadInterval = TimeSpan.Zero;
        options.KeyOverlapPeriod = TimeSpan.FromMinutes(1);
    }, builder.Configuration.GetSection("ReverseProxy"));

    await using var app = builder.Build();
    app.UseAuthorization();
    app.MapKunJwks(context => !context.Request.Query.ContainsKey("blocked"));
    var internalGateCalls = 0;
    app.MapKunInternalJwks("/internal/jwks-probe", context =>
    {
        internalGateCalls++;
        return !context.Request.Query.ContainsKey("blocked");
    });
    await app.StartAsync();
    try
    {
        using var client = new HttpClient(new SocketsHttpHandler { UseProxy = false }) { BaseAddress = new Uri(app.Urls.Single()) };
        // AddKunGateway 装配的回退授权策略要求认证；两个 JWKS 端点仍应允许匿名。
        var initial = await ReadKeysAsync(client, "/.well-known/jwks.json");
        Require(initial.Length == 1, "initial public key");
        await ExpectNotFoundAsync(client, "/.well-known/jwks.json?blocked");
        await ExpectNotFoundAsync(client, "/internal/jwks-probe");
        Require(internalGateCalls == 0, "production opt-in must be checked before additionalGate");

        app.Services.GetRequiredService<IOptions<KunGatewayOptions>>().Value.ExposeInternalJwks = true;
        Require((await ReadKeysAsync(client, "/internal/jwks-probe")).SequenceEqual(initial), "internal public key");
        await ExpectNotFoundAsync(client, "/internal/jwks-probe?blocked");

        using var nextKey = RSA.Create(2048);
        var replacement = privateKey + ".next";
        File.WriteAllText(replacement, nextKey.ExportRSAPrivateKeyPem());
        File.Move(replacement, privateKey, overwrite: true);
        var overlapping = await ReadKeysAsync(client, "/.well-known/jwks.json");
        Require(overlapping.Length == 2 && overlapping.Contains(initial[0]), "rotation must retain the old public key");
        Require((await ReadKeysAsync(client, "/internal/jwks-probe")).SequenceEqual(overlapping), "both endpoints must share the overlap ring");

        clock.Advance(TimeSpan.FromMinutes(2));
        var retired = await ReadKeysAsync(client, "/.well-known/jwks.json");
        Require(retired.Length == 1 && retired[0] != initial[0], "old key must retire after overlap");
        Require((await ReadKeysAsync(client, "/internal/jwks-probe")).SequenceEqual(retired), "internal endpoint must retire the same key");
        Console.WriteLine("Kun.Gateway package NativeAOT: anonymous JWKS, gates, no-store and key rotation passed.");
    }
    finally
    {
        await app.StopAsync();
    }
}
finally
{
    Directory.Delete(directory, recursive: true);
}

static async Task ExpectNotFoundAsync(HttpClient client, string path)
{
    using var response = await client.GetAsync(path);
    Require(response.StatusCode == HttpStatusCode.NotFound, $"404 gate: {path}");
}

static async Task<string[]> ReadKeysAsync(HttpClient client, string path)
{
    using var response = await client.GetAsync(path);
    Require(response.StatusCode == HttpStatusCode.OK, $"anonymous JWKS: {path}");
    Require(response.Headers.CacheControl?.NoStore == true, $"no-store: {path}");
    using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    return json.RootElement.GetProperty("keys").EnumerateArray().Select(key =>
    {
        Require(key.GetProperty("kty").GetString() == "RSA", "RSA key");
        Require(key.GetProperty("alg").GetString() == "RS256", "RS256 algorithm");
        Require(key.GetProperty("use").GetString() == "sig", "signing key");
        foreach (var property in new[] { "d", "p", "q", "dp", "dq", "qi" })
            Require(!key.TryGetProperty(property, out _), "JWKS must not contain private parameters");
        return key.GetProperty("kid").GetString() ?? throw new InvalidOperationException("Missing kid");
    }).ToArray();
}

static void Require(bool condition, string assertion)
{
    if (!condition) throw new InvalidOperationException(assertion);
}

internal sealed class ProbeClock : TimeProvider
{
    private DateTimeOffset _now = DateTimeOffset.UtcNow;
    public override DateTimeOffset GetUtcNow() => _now;
    internal void Advance(TimeSpan value) => _now += value;
}
