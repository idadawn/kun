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
    [Theory]
    [InlineData("user", true)]
    [InlineData("service", false)]
    [InlineData("agent", false)]
    public async Task AllRpcShapes_SameSubjectId_PreserveKindBeforeHandler(string kind, bool expected)
    {
        var checker = Substitute.For<IPermissionChecker>(); var seen = new List<string>();
#pragma warning disable CA2012 // 只授予 user，验证服务主体不能借用同名用户权限。
        checker.CheckAsync(Arg.Any<PermissionRequest>(), Arg.Any<CancellationToken>()).Returns(info =>
        {
            var subject = info.Arg<PermissionRequest>().Subject; seen.Add(subject.Kind);
            Assert.Equal("same-id", subject.Id); Assert.Equal(kind, subject.Kind);
            return ValueTask.FromResult(new PermissionDecision(subject.Kind == "user", "user-only-grant", []));
        });
#pragma warning restore CA2012
        using var services = new ServiceCollection().AddSingleton(checker).BuildServiceProvider();
        var http = new DefaultHttpContext
        {
            RequestServices = services,
            User = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim("sub", "same-id"), new Claim("tenant_id", "tenant-a"), new Claim("principal_kind", kind)], "validated"))
        };
        http.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(new RequiresPermissionAttribute("sample:item:read")), "test"));
        var context = Substitute.For<ServerCallContext>(); context.UserState.Returns(new Dictionary<object, object> { ["__HttpContext"] = http });
        var interceptor = new KunPermissionInterceptor(); var reader = Substitute.For<IAsyncStreamReader<string>>(); var writer = Substitute.For<IServerStreamWriter<string>>();
        var calls = 0;
        Func<Task>[] requests = [
            async () => await interceptor.UnaryServerHandler("request", context, (_, _) => { calls++; return Task.FromResult("ok"); }),
            () => interceptor.ServerStreamingServerHandler("request", writer, context, (_, _, _) => { calls++; return Task.CompletedTask; }),
            async () => await interceptor.ClientStreamingServerHandler(reader, context, (_, _) => { calls++; return Task.FromResult("ok"); }),
            () => interceptor.DuplexStreamingServerHandler(reader, writer, context, (_, _, _) => { calls++; return Task.CompletedTask; })
        ];
        foreach (var request in requests)
            if (expected) await request(); else Assert.Equal(StatusCode.PermissionDenied, (await Assert.ThrowsAsync<RpcException>(request)).StatusCode);
        Assert.Equal(expected ? 4 : 0, calls); Assert.Equal(4, seen.Count);
        await reader.DidNotReceive().MoveNext(Arg.Any<CancellationToken>());
    }

}
