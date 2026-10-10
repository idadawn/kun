using System.Collections.Concurrent;
using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using FluentAssertions;
using global::Grpc.Core;
using global::Grpc.Net.Client;
using Kun.Core.Results;
using Kun.Grpc.DependencyInjection;
using Kun.IntegrationTests.Grpc.Generated;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Kun.IntegrationTests.Grpc;

public sealed class StreamingInterceptorsTests
{
    [Theory]
    [InlineData("unary")]
    [InlineData("server")]
    [InlineData("client")]
    [InlineData("duplex")]
    public async Task Rpc_WithoutTenant_RejectsBeforeHandlerAndFirstFrame(string shape)
    {
        await using var host = await ProbeHost.StartAsync();
        var call = () => host.InvokeAsync(shape, tenant: null);
        var failure = await call.Should().ThrowAsync<RpcException>();
        failure.Which.StatusCode.Should().Be(StatusCode.Unauthenticated);
        host.Calls.Executed.Should().Be(0);
        host.Calls.Read.Should().Be(0);
    }

    [Theory]
    [InlineData("unary")]
    [InlineData("server")]
    [InlineData("client")]
    [InlineData("duplex")]
    public async Task Rpc_WithTenant_RecordsTraceThroughFinalFrameAndMapsResult(string shape)
    {
        await using var host = await ProbeHost.StartAsync();
        var value = await host.InvokeAsync(shape, "tenant-a");
        value.Should().Be("ok");
        host.Calls.Executed.Should().Be(1);
        host.Calls.ActivityNames.Should().OnlyContain(name => name == "grpc.server");
        var methodSuffix = "/" + char.ToUpperInvariant(shape[0]) + shape[1..];
        host.Spans.Should().Contain(span => span.Method.EndsWith(methodSuffix, StringComparison.Ordinal) && span.Kind == ActivityKind.Server);
        host.Calls.Fail = true;
        var failed = () => host.InvokeAsync(shape, "tenant-a");
        var error = await failed.Should().ThrowAsync<RpcException>();
        error.Which.StatusCode.Should().Be(StatusCode.NotFound);
        error.Which.Trailers.Should().Contain(entry => entry.Key == "kun-error-code" && entry.Value == "probe.missing");
    }

    private sealed class Calls
    {
        internal int Executed;
        internal int Read;
        internal bool Fail;
        internal ConcurrentQueue<string?> ActivityNames { get; } = [];
    }

    private sealed class ProbeService(Calls calls) : StreamProbe.StreamProbeBase
    {
        private void Enter()
        {
            Interlocked.Increment(ref calls.Executed);
            calls.ActivityNames.Enqueue(Activity.Current?.OperationName);
            if (calls.Fail) Result<string>.Fail(Error.NotFound("probe.missing", "Missing probe")).EnsureSuccess();
        }
        public override Task<Probe> Unary(Probe request, ServerCallContext context) { Enter(); return Task.FromResult(new Probe { Value = request.Value }); }
        public override async Task Server(Probe request, IServerStreamWriter<Probe> responseStream, ServerCallContext context)
        {
            Enter(); await Task.Yield(); calls.ActivityNames.Enqueue(Activity.Current?.OperationName); await responseStream.WriteAsync(new Probe { Value = request.Value }, context.CancellationToken);
        }
        public override async Task<Probe> Client(IAsyncStreamReader<Probe> requestStream, ServerCallContext context)
        {
            Enter(); var value = "";
            while (await requestStream.MoveNext(context.CancellationToken)) { Interlocked.Increment(ref calls.Read); value = requestStream.Current.Value; calls.ActivityNames.Enqueue(Activity.Current?.OperationName); }
            return new Probe { Value = value };
        }
        public override async Task Duplex(IAsyncStreamReader<Probe> requestStream, IServerStreamWriter<Probe> responseStream, ServerCallContext context)
        {
            Enter(); while (await requestStream.MoveNext(context.CancellationToken)) { Interlocked.Increment(ref calls.Read); calls.ActivityNames.Enqueue(Activity.Current?.OperationName); await responseStream.WriteAsync(requestStream.Current, context.CancellationToken); }
        }
    }

    private sealed class ProbeHost(WebApplication app, GrpcChannel channel, RSA key, ActivityListener listener, Calls calls, ConcurrentQueue<(string Method, ActivityKind Kind)> spans) : IAsyncDisposable
    {
        internal Calls Calls => calls;
        internal ConcurrentQueue<(string Method, ActivityKind Kind)> Spans => spans;
        internal static async Task<ProbeHost> StartAsync()
        {
            var key = RSA.Create(2048); var calls = new Calls();
            var spans = new ConcurrentQueue<(string Method, ActivityKind Kind)>();
            var listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == "Kun.Grpc",
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = activity => spans.Enqueue((activity.GetTagItem("rpc.method")?.ToString() ?? "", activity.Kind))
            };
            ActivitySource.AddActivityListener(listener);
            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0, listen => listen.Protocols = HttpProtocols.Http2));
            builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = "test-gateway",
                    ValidateAudience = true,
                    ValidAudience = "test-service",
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new RsaSecurityKey(key),
                    ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
                    ClockSkew = TimeSpan.Zero
                };
            });
            builder.Services.AddKunGrpc(); builder.Services.AddSingleton(calls);
            var app = builder.Build(); app.UseAuthentication(); app.MapGrpcService<ProbeService>();
            await app.StartAsync(TestContext.Current.CancellationToken);
            var channel = GrpcChannel.ForAddress(app.Urls.Single(), new GrpcChannelOptions { HttpHandler = new SocketsHttpHandler { UseProxy = false } });
            return new ProbeHost(app, channel, key, listener, calls, spans);
        }
        internal async Task<string> InvokeAsync(string shape, string? tenant)
        {
            var claims = new List<Claim> { new("sub", "test-user") };
            if (tenant is not null) claims.Add(new Claim("tenant_id", tenant));
            var now = DateTime.UtcNow;
            var token = new JwtSecurityTokenHandler().CreateEncodedJwt(new SecurityTokenDescriptor
            {
                Issuer = "test-gateway",
                Audience = "test-service",
                Subject = new ClaimsIdentity(claims),
                IssuedAt = now,
                NotBefore = now,
                Expires = now.AddMinutes(1),
                SigningCredentials = new SigningCredentials(new RsaSecurityKey(key), SecurityAlgorithms.RsaSha256)
            });
            var headers = new Metadata { { "authorization", "Bearer " + token } };
            var client = new StreamProbe.StreamProbeClient(channel); var request = new Probe { Value = "ok" };
            switch (shape)
            {
                case "unary": return (await client.UnaryAsync(request, headers, cancellationToken: TestContext.Current.CancellationToken)).Value;
                case "server":
                    using (var stream = client.Server(request, headers, cancellationToken: TestContext.Current.CancellationToken))
                    { var output = ""; while (await stream.ResponseStream.MoveNext(TestContext.Current.CancellationToken)) output = stream.ResponseStream.Current.Value; return output; }
                case "client":
                    using (var stream = client.Client(headers, cancellationToken: TestContext.Current.CancellationToken))
                    { await stream.RequestStream.WriteAsync(request, TestContext.Current.CancellationToken); await stream.RequestStream.CompleteAsync(); return (await stream.ResponseAsync).Value; }
                default:
                    using (var stream = client.Duplex(headers, cancellationToken: TestContext.Current.CancellationToken))
                    { await stream.RequestStream.WriteAsync(request, TestContext.Current.CancellationToken); await stream.RequestStream.CompleteAsync(); var output = ""; while (await stream.ResponseStream.MoveNext(TestContext.Current.CancellationToken)) output = stream.ResponseStream.Current.Value; return output; }
            }
        }
        public async ValueTask DisposeAsync() { channel.Dispose(); await app.DisposeAsync(); listener.Dispose(); key.Dispose(); }
    }
}
