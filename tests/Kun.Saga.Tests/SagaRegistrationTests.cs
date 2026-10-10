using System.Text.Json.Serialization;
using Kun.Saga.Abstractions;
using Kun.Saga.DependencyInjection;
using Kun.Saga.Persistence;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Kun.Saga.Tests;

public sealed class SagaRegistrationTests
{
    [Fact]
    public async Task Registration_DoesNotConstructServices_AndResolvesSagaPerScopeByStableName()
    {
        var counters = new ScopeCounters();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(counters);
        services.AddScoped<ScopedDependency>();
        services.AddScoped<ISagaInstanceStore>(_ => Substitute.For<ISagaInstanceStore>());
        services.AddKunSaga(builder => builder.AddSaga<ScopedSaga, ScopedContext>(RegistrationJsonContext.Default.ScopedContext));
        Assert.Equal(0, counters.Created);
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        Assert.Equal(0, counters.Created);
        Guid first;
        await using (var scope = provider.CreateAsyncScope())
        {
            var saga = scope.ServiceProvider.GetRequiredService<ScopedSaga>();
            first = saga.Dependency.Id;
            var registry = scope.ServiceProvider.GetRequiredService<SagaRegistry>();
            Assert.Equal(["stable.order.v1"], registry.RegisteredNames);
            var registration = Assert.IsType<SagaRegistration>(registry.Find("stable.order.v1"));
            Assert.Equal(1, registration.StepCount);
            await scope.ServiceProvider.GetRequiredService<ISagaOrchestrator>().StartAsync<ScopedSaga, ScopedContext>(new ScopedContext(), TestContext.Current.CancellationToken);
            await scope.ServiceProvider.GetRequiredService<ISagaInstanceStore>().Received(1).AddAsync(Arg.Is<SagaInstance>(instance => instance.SagaName == "stable.order.v1"), Arg.Any<CancellationToken>());
        }
        Assert.Equal(1, counters.Disposed);
        await using (var scope = provider.CreateAsyncScope())
        {
            Assert.NotEqual(first, scope.ServiceProvider.GetRequiredService<ScopedSaga>().Dependency.Id);
        }
        Assert.Equal(2, counters.Created);
        Assert.Equal(2, counters.Disposed);
    }
}

internal sealed class ScopeCounters { internal int Created; internal int Disposed; }
internal sealed class ScopedDependency : IDisposable
{
    private readonly ScopeCounters _counters;
    internal Guid Id { get; } = Guid.NewGuid();
    public ScopedDependency(ScopeCounters counters) { _counters = counters; counters.Created++; }
    public void Dispose() => _counters.Disposed++;
}
internal sealed class ScopedSaga(ScopedDependency dependency) : ISaga<ScopedContext>, ISagaStep<ScopedContext>
{
    public string Name => "stable.order.v1";
    public TimeSpan DefaultStepTimeout => TimeSpan.FromSeconds(30);
    public IReadOnlyList<ISagaStep<ScopedContext>> Steps => [this];
    public TimeSpan? Timeout => null;
    internal ScopedDependency Dependency => dependency;
    public ValueTask ExecuteAsync(ScopedContext context, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    public ValueTask CompensateAsync(ScopedContext context, CancellationToken cancellationToken) => ValueTask.CompletedTask;
}
internal sealed class ScopedContext : ISagaContext
{
    public Guid SagaId { get; init; } = Guid.NewGuid();
    public string? TenantId { get; init; }
}
[JsonSerializable(typeof(ScopedContext))]
internal sealed partial class RegistrationJsonContext : JsonSerializerContext;
