using System.Security.Claims;
using Grpc.Core;
using Kun.Authorization;
using Kun.Grpc.Interceptors;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Kun.Grpc.Tests;

public sealed class PermissionInterceptorTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AllRpcShapes_AuthorizeBeforeReadingOrExecuting(bool allow)
    {
        var services = new ServiceCollection();
        if (allow)
        {
            var checker = Substitute.For<IPermissionChecker>();
#pragma warning disable CA2012 // 设置 mock 的 ValueTask 返回值。
            checker.CheckAsync(Arg.Any<PermissionRequest>(), Arg.Any<CancellationToken>())
                .Returns(ValueTask.FromResult(new PermissionDecision(true, "test", [])));
#pragma warning restore CA2012
            services.AddSingleton(checker);
        }
        using var provider = services.BuildServiceProvider();
        var http = new DefaultHttpContext
        {
            RequestServices = provider,
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "user-1")], "test"))
        };
        http.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(new RequiresPermissionAttribute("sample:item:read")), "test"));
        var context = Substitute.For<ServerCallContext>();
        context.UserState.Returns(new Dictionary<object, object> { ["__HttpContext"] = http });
        var interceptor = new KunPermissionInterceptor();
        var reader = Substitute.For<IAsyncStreamReader<string>>();
        var writer = Substitute.For<IServerStreamWriter<string>>();
        var executed = 0;
        Task<string> Unary(string _, ServerCallContext __) { executed++; return Task.FromResult("ok"); }
        Task Server(string _, IServerStreamWriter<string> __, ServerCallContext ___) { executed++; return Task.CompletedTask; }
        Task<string> Client(IAsyncStreamReader<string> _, ServerCallContext __) { executed++; return Task.FromResult("ok"); }
        Task Duplex(IAsyncStreamReader<string> _, IServerStreamWriter<string> __, ServerCallContext ___) { executed++; return Task.CompletedTask; }
        Func<Task>[] invoke = [
            async () => await interceptor.UnaryServerHandler("request", context, Unary),
            () => interceptor.ServerStreamingServerHandler("request", writer, context, Server),
            async () => await interceptor.ClientStreamingServerHandler(reader, context, Client),
            () => interceptor.DuplexStreamingServerHandler(reader, writer, context, Duplex)
        ];
        foreach (var call in invoke)
        {
            if (allow) await call();
            else Assert.Equal(StatusCode.PermissionDenied, (await Assert.ThrowsAsync<RpcException>(call)).StatusCode);
        }
        Assert.Equal(allow ? 4 : 0, executed);
        await reader.DidNotReceive().MoveNext(Arg.Any<CancellationToken>());
    }
}
