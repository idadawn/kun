using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Kun.AspNetCore.Authorization;
using Kun.AspNetCore.DependencyInjection;
using Kun.Authorization;
using Kun.Core.Results;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Kun.IntegrationTests.Authorization;

public sealed class PermissionEndpointTests
{
    [Theory]
    [InlineData(null, HttpStatusCode.Forbidden)]
    [InlineData(false, HttpStatusCode.Forbidden)]
    [InlineData(true, HttpStatusCode.OK)]
    public async Task ProtectedHttpEndpoint_MissingOrDenyingEngineNeverExecutesHandler(bool? allow, HttpStatusCode expected)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddAuthentication("test").AddScheme<AuthenticationSchemeOptions, TestAuthentication>("test", _ => { });
        builder.Services.AddKunAuthorization();
        var calls = new List<string>();
        if (allow is not null)
        {
            var checker = Substitute.For<IPermissionChecker>();
#pragma warning disable CA2012 // 设置 mock 回调，不消费业务 ValueTask。
            checker.CheckAsync(Arg.Any<PermissionRequest>(), Arg.Any<CancellationToken>()).Returns(info =>
            {
                var request = info.Arg<PermissionRequest>();
                Assert.Equal("user-1", request.Subject.Id);
                Assert.Equal("tenant-a", request.Subject.TenantId);
                calls.Add("check");
                return ValueTask.FromResult(new PermissionDecision(allow.Value, "test", []));
            });
#pragma warning restore CA2012
            builder.Services.AddSingleton(checker);
        }
        await using var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGet("/protected", () => { calls.Add("handler"); return "ok"; }).RequireKunPermission("sample:item:read");
        await app.StartAsync(TestContext.Current.CancellationToken);
        var response = await app.GetTestClient().GetAsync("/protected", TestContext.Current.CancellationToken);
        Assert.Equal(expected, response.StatusCode);
        if (allow == true) Assert.Equal(["check", "handler"], calls);
        else Assert.DoesNotContain("handler", calls);
    }

    [Fact]
    public async Task KunException_UsesHostProblemDetailsWriter()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IProblemDetailsWriter, MarkerWriter>();
        builder.Services.AddKunAspNetCore();
        await using var app = builder.Build();
        app.UseExceptionHandler();
        app.MapGet("/failure", () => ThrowFailure());
        await app.StartAsync(TestContext.Current.CancellationToken);
        var response = await app.GetTestClient().GetAsync("/failure", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("custom:item.conflict", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    private static IResult ThrowFailure() => throw new ResultFailureException(Error.Conflict("item.conflict", "冲突"));

    private sealed class MarkerWriter : IProblemDetailsWriter
    {
        public bool CanWrite(ProblemDetailsContext context) => true;
        public async ValueTask WriteAsync(ProblemDetailsContext context)
        {
            Assert.IsType<ResultFailureException>(context.Exception);
            await context.HttpContext.Response.WriteAsync("custom:" + ((ResultFailureException)context.Exception).Error.Code);
        }
    }

    private sealed class TestAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(
            new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "user-1"), new Claim("tenant_id", "tenant-a")], "test")), "test")));
    }
}
