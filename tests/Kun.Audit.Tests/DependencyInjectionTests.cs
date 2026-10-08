#pragma warning disable KUNAUDIT001 // 仅保留旧 API 兼容测试／历史基线；新宿主使用同事务入口。
using FluentAssertions;
using Kun.Audit.Abstractions;
using Kun.Audit.DependencyInjection;
using Kun.Audit.EntityFrameworkCore;
using Kun.Audit.EntityFrameworkCore.DependencyInjection;
using Kun.Core.MultiTenancy;
using Kun.Core.Security;
using Kun.Core.Time;
using Kun.EventBus;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Kun.Audit.Tests;

public sealed class DependencyInjectionTests
{
    [Fact]
    public void AddKunAudit_RegistersNullWriterByDefault()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddKunAudit();

        var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IAuditWriter>().Should().BeOfType<NullAuditWriter>();
    }

    [Fact]
    public void AddKunAudit_UseEventBus_ReplacesWriterWithEventBusImplementation()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<IIntegrationEventBus>());

        services.AddKunAudit().UseEventBus();

        var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IAuditWriter>().Should().BeOfType<EventBusAuditWriter>();
    }

    [Fact]
    public void AddKunAuditEntityFrameworkCore_WithUseEntityFramework_RegistersInterceptorAndTypedEfWriter()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<ITenantContext>());
        services.AddSingleton(Substitute.For<ICurrentUser>());
        services.AddDbContext<TestDbContext>(opts => opts.UseInMemoryDatabase("di-test"));

        services.AddKunAudit()
            .AddKunAuditEntityFrameworkCore()
            .UseEntityFramework<TestDbContext>();

        using var scope = services.BuildServiceProvider().CreateScope();
        scope.ServiceProvider.GetRequiredService<IAuditWriter>().Should().BeOfType<EfAuditWriter<TestDbContext>>();
        scope.ServiceProvider.GetRequiredService<AuditSaveChangesInterceptor>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<ISystemClock>().Should().BeSameAs(SystemClock.Instance);
    }

    private sealed class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options);
}
