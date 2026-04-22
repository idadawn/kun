using FluentAssertions;
using Kun.Core.MultiTenancy;
using Kun.EntityFrameworkCore;
using Kun.EntityFrameworkCore.DependencyInjection;
using Kun.EventBus;
using Kun.EventBus.Outbox;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Kun.IntegrationTests.Outbox;

// Pure DI-container tests for the IOutboxWriter primitive introduced in P0-4.
// No database is touched — these assertions only probe that the container
// registration wires IIntegrationEventBus and IOutboxWriter to the same
// scoped instance of IntegrationEventOutboxAccumulator.

public sealed class OutboxWriterDependencyInjectionTests
{
    [Fact]
    public void AddKunEntityFrameworkCore_RegistersIOutboxWriter()
    {
        var provider = BuildProvider();
        using var scope = provider.CreateScope();

        var writer = scope.ServiceProvider.GetService<IOutboxWriter>();

        writer.Should().NotBeNull();
        writer.Should().BeOfType<IntegrationEventOutboxAccumulator>();
    }

    [Fact]
    public void IOutboxWriter_And_IIntegrationEventBus_ResolveToSameInstance_PerScope()
    {
        var provider = BuildProvider();
        using var scope = provider.CreateScope();

        var writer = scope.ServiceProvider.GetRequiredService<IOutboxWriter>();
        var bus = scope.ServiceProvider.GetRequiredService<IIntegrationEventBus>();

        writer.Should().BeSameAs(bus);
    }

    [Fact]
    public void IOutboxWriter_ResolvesToFreshInstance_AcrossScopes()
    {
        var provider = BuildProvider();

        IOutboxWriter writerA;
        IOutboxWriter writerB;

        using (var scopeA = provider.CreateScope())
        {
            writerA = scopeA.ServiceProvider.GetRequiredService<IOutboxWriter>();
        }
        using (var scopeB = provider.CreateScope())
        {
            writerB = scopeB.ServiceProvider.GetRequiredService<IOutboxWriter>();
        }

        writerA.Should().NotBeSameAs(writerB);
    }

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();

        // Minimal dependencies for the accumulator: tenant context + event registry.
        services.AddSingleton<ITenantContext>(NullTenantContext.Instance);
        services.AddSingleton<IEventTypeRegistry>(new FakeEventTypeRegistry());

        // A DbContextOptions is required by the EfUnitOfWork registration path;
        // we never call SaveChanges here so an in-memory InMemory provider would be
        // overkill — register a minimal KunDbContext-less variant by supplying the
        // options via a no-op AddDbContextOptions. Since EfUnitOfWork is unused in
        // these DI-only assertions, we skip AddDbContext entirely.
        services.AddKunEntityFrameworkCore();

        return services.BuildServiceProvider(validateScopes: true);
    }

    private sealed class FakeEventTypeRegistry : IEventTypeRegistry
    {
        public Type? Resolve(string eventType) => null;
        public string? GetEventType(Type clrType) => null;
        public IReadOnlyCollection<(string EventId, Type ClrType)> GetAll() => [];
        public System.Text.Json.Serialization.Metadata.JsonTypeInfo? GetJsonTypeInfo(Type clrType) => null;
    }
}
