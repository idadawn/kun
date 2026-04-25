using System.Net;
using FluentAssertions;
using Kun.Auditing.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace Kun.Auditing.Tests;

public sealed class AuditContextMiddlewareTests
{
    [Fact]
    public async Task Captures_remote_ip_user_agent_and_trace_id_from_HttpContext()
    {
        var auditContext = new AuditContext();
        var middleware = new AuditContextMiddleware(auditContext);

        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.42");
        httpContext.Request.Headers.UserAgent = new StringValues("Mozilla/5.0 TestAgent");

        using var activity = new System.Diagnostics.Activity("test-op");
        activity.Start();
        try
        {
            var called = false;
            RequestDelegate next = _ => { called = true; return Task.CompletedTask; };

            await middleware.InvokeAsync(httpContext, next);

            called.Should().BeTrue();
            auditContext.IpAddress.Should().Be("203.0.113.42");
            auditContext.UserAgent.Should().Be("Mozilla/5.0 TestAgent");
            auditContext.TraceId.Should().Be(activity.TraceId.ToString());
        }
        finally
        {
            activity.Stop();
        }
    }

    [Fact]
    public async Task Leaves_fields_null_when_HttpContext_has_none_of_them()
    {
        var auditContext = new AuditContext();
        var middleware = new AuditContextMiddleware(auditContext);

        var httpContext = new DefaultHttpContext();
        // No RemoteIpAddress, no User-Agent header.

        RequestDelegate next = _ => Task.CompletedTask;

        await middleware.InvokeAsync(httpContext, next);

        auditContext.IpAddress.Should().BeNull();
        // Missing header yields empty string from StringValues.ToString().
        auditContext.UserAgent.Should().Be(string.Empty);
    }
}
