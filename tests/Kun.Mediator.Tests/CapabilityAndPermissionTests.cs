using System.Text.Json.Serialization;
using FluentAssertions;
using Kun.Authorization;
using Kun.Core.Cqrs;
using Kun.Core.MultiTenancy;
using Kun.Core.Results;
using Kun.Core.Security;
using Kun.Mediator.Behaviors;
using Kun.Mediator.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Kun.Mediator.Tests;

internal sealed record DirectoryCommand(string Name) : IRequest<int>;
internal sealed class DirectoryHandler : IRequestHandler<DirectoryCommand, int>
{
    public ValueTask<int> Handle(DirectoryCommand request, CancellationToken cancellationToken) => ValueTask.FromResult(request.Name.Length);
}
internal sealed record ProtectedCommand : IRequest<Result<int>>, IPermissionProtectedRequest
{
    public static IReadOnlyList<string> RequiredPermissions => ["sample:record:write"];
}
internal sealed class ProtectedHandler(List<string> calls) : IRequestHandler<ProtectedCommand, Result<int>>
{
    public ValueTask<Result<int>> Handle(ProtectedCommand request, CancellationToken cancellationToken)
    {
        calls.Add("handler");
        return ValueTask.FromResult(Result<int>.Ok(42));
    }
}
[JsonSerializable(typeof(DirectoryCommand))]
[JsonSerializable(typeof(int))]
internal sealed partial class DirectoryJsonContext : JsonSerializerContext;

public sealed class CapabilityAndPermissionTests
{
    [Fact]
    public async Task RegisteredHandler_EnumeratesStableIdAndSchemas_AndStillDispatches()
    {
        var services = new ServiceCollection();
        services.AddKunMediator(builder => builder.AddRequestHandler<DirectoryCommand, int, DirectoryHandler>(
            "sample:count", DirectoryJsonContext.Default.DirectoryCommand, DirectoryJsonContext.Default.Int32));
        await using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<ICapabilityRegistry>();
        var entry = Assert.Single(registry.GetAll());
        Assert.Same(entry, registry.Find("sample:count"));
        Assert.Equal("object", (string?)entry.RequestSchema["type"]);
        Assert.Equal("string", (string?)entry.RequestSchema["properties"]?["Name"]?["type"]);
        Assert.Equal("integer", (string?)entry.ResponseSchema["type"]);
        Assert.Equal("https://json-schema.org/draft/2020-12/schema", (string?)entry.RequestSchema["$schema"]);
        Assert.Equal(4, await provider.GetRequiredService<ISender>().Send(new DirectoryCommand("test"), TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PermissionCheck_PrecedesHandler_AndMissingEngineIsDenied(bool? allow)
    {
        var calls = new List<string>();
        var services = new ServiceCollection();
        services.AddSingleton(calls);
        var user = Substitute.For<ICurrentUser>();
        user.IsAuthenticated.Returns(true);
        user.UserId.Returns("user-1");
        user.PrincipalKind.Returns("user");
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns("tenant-a");
        services.AddSingleton(user);
        services.AddSingleton(tenant);
        if (allow is not null)
        {
            var checker = Substitute.For<IPermissionChecker>();
#pragma warning disable CA2012 // 此处配置 NSubstitute，不执行或重复消费业务 ValueTask。
            checker.CheckAsync(Arg.Any<PermissionRequest>(), Arg.Any<CancellationToken>()).Returns(info =>
            {
                calls.Add("check");
                var request = info.Arg<PermissionRequest>();
                Assert.Equal("sample:record:write", request.Permission);
                Assert.Equal("tenant-a", request.Subject.TenantId);
                return ValueTask.FromResult(new PermissionDecision(allow.Value, "test", []));
            });
#pragma warning restore CA2012
            services.AddSingleton(checker);
        }
        services.AddKunMediator(builder => builder
            .AddRequestHandler<ProtectedCommand, Result<int>, ProtectedHandler>()
            .AddPermissionBehavior<ProtectedCommand, Result<int>>(Result<int>.Fail));
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        var result = await provider.GetRequiredService<ISender>().Send(new ProtectedCommand(), TestContext.Current.CancellationToken);
        Assert.Equal(allow == true, result.IsSuccess);
        if (allow == true) Assert.Equal(["check", "handler"], calls);
        else
        {
            Assert.Equal(ErrorType.Forbidden, result.Error.Type);
            Assert.DoesNotContain("handler", calls);
        }
    }

    [Fact]
    public void ProtectedHandler_WithoutPermissionBehavior_MediatorRefusesToBuild()
    {
        var calls = new List<string>();
        var services = new ServiceCollection();
        services.AddSingleton(calls);
        services.AddKunMediator(builder => builder.AddRequestHandler<ProtectedCommand, Result<int>, ProtectedHandler>());
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        var error = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<ISender>());

        Assert.Contains(typeof(ProtectedCommand).FullName!, error.Message, StringComparison.Ordinal);
        Assert.Contains("AddPermissionBehavior", error.Message, StringComparison.Ordinal);
        Assert.Empty(calls);
    }

    [Fact]
    public void ProtectedHandler_RegisteredByReflection_IsStillRefusedBeforeAnyHandlerRuns()
    {
        // 编译期门禁拦不住反射注册；运行期兜底必须让 Mediator 整体拒绝构建，而不是放行处理器。
        var calls = new List<string>();
        var services = new ServiceCollection();
        services.AddSingleton(calls);
        services.AddKunMediator(builder => typeof(KunMediatorBuilder).GetMethods()
            .Single(method => method.Name == nameof(KunMediatorBuilder.AddRequestHandler) && method.GetParameters().Length == 0)
            .MakeGenericMethod(typeof(ProtectedCommand), typeof(Result<int>), typeof(ProtectedHandler))
            .Invoke(builder, null));
        using var provider = services.BuildServiceProvider();

        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<ISender>());
        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IPublisher>());
        Assert.Empty(calls);
    }

    [Fact]
    public async Task PermissionCheck_WithoutOptionalPorts_StartsWithValidationAndDenies()
    {
        var calls = new List<string>();
        var services = new ServiceCollection();
        services.AddSingleton(calls);
        services.AddKunMediator(builder => builder
            .AddRequestHandler<ProtectedCommand, Result<int>, ProtectedHandler>()
            .AddPermissionBehavior<ProtectedCommand, Result<int>>(Result<int>.Fail)
            .AddPermissionBehavior<ProtectedCommand, Result<int>>(Result<int>.Fail));

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });
        var result = await provider.GetRequiredService<ISender>().Send(new ProtectedCommand(), TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorType.Unauthorized, result.Error.Type);
        Assert.Empty(calls);
    }

    [Theory]
    [InlineData("user", true)]
    [InlineData("service", false)]
    [InlineData("agent", false)]
    [InlineData("unknown", false)]
    public async Task PermissionCheck_SameIdDifferentKinds_NeverUsesUserGrantForService(string kind, bool expected)
    {
        var calls = new List<string>(); var services = new ServiceCollection(); services.AddSingleton(calls);
        var user = Substitute.For<ICurrentUser>(); user.IsAuthenticated.Returns(true); user.UserId.Returns("shared-id"); user.PrincipalKind.Returns(kind);
        var tenant = Substitute.For<ITenantContext>(); tenant.TenantId.Returns("tenant-a");
        var checker = Substitute.For<IPermissionChecker>();
#pragma warning disable CA2012 // 配置真实管道所消费的判定端口，检查主体元组。
        checker.CheckAsync(Arg.Any<PermissionRequest>(), Arg.Any<CancellationToken>()).Returns(info =>
        {
            var subject = info.Arg<PermissionRequest>().Subject; subject.Id.Should().Be("shared-id"); subject.Kind.Should().Be(kind);
            calls.Add("check"); return ValueTask.FromResult(new PermissionDecision(subject.Kind == "user", "user-only-grant", []));
        });
#pragma warning restore CA2012
        services.AddSingleton(user); services.AddSingleton(tenant); services.AddSingleton(checker);
        services.AddKunMediator(builder => builder.AddRequestHandler<ProtectedCommand, Result<int>, ProtectedHandler>()
            .AddPermissionBehavior<ProtectedCommand, Result<int>>(Result<int>.Fail));
        await using var provider = services.BuildServiceProvider();
        var result = await provider.GetRequiredService<ISender>().Send(new ProtectedCommand(), TestContext.Current.CancellationToken);
        result.IsSuccess.Should().Be(expected);
        calls.Contains("handler", StringComparer.Ordinal).Should().Be(expected);
        if (kind == "unknown") calls.Should().BeEmpty();
    }

}
