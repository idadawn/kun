using Kun.Saga.Abstractions;
using Kun.Saga.Orchestration;
using Kun.Saga.Persistence;
using Kun.Saga.Persistence.Ef;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kun.Saga.DependencyInjection;

/// <summary>
/// Extension methods for <see cref="IServiceCollection"/> to register Kun Saga services.
/// </summary>
public static class KunSagaServiceCollectionExtensions
{
    /// <summary>
    /// Adds Kun Saga orchestration services to the dependency injection container.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="configure">
    /// A delegate to configure the <see cref="KunSagaBuilder"/>, used to register saga types
    /// and their step implementations.
    /// </param>
    /// <returns>The same <paramref name="services"/> for fluent chaining.</returns>
    public static IServiceCollection AddKunSaga(
        this IServiceCollection services,
        Action<KunSagaBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var builder = new KunSagaBuilder(services);
        configure(builder);

        // Register the saga registry as a singleton built from all registered sagas.
        var registry = builder.BuildRegistry();
        services.AddSingleton(registry);

        // Register core services.
        services.TryAddScoped<ISagaInstanceStore, EfSagaInstanceStore>();
        services.TryAddSingleton<ISagaOrchestrator, SagaOrchestrator>();

        // Register executor options with defaults.
        services.TryAddSingleton(new SagaStepExecutorOptions());
        services.TryAddSingleton(new SagaTimeoutDetectorOptions());

        // Register background services.
        services.AddHostedService<SagaStepExecutor>();
        services.AddHostedService<SagaTimeoutDetector>();

        return services;
    }
}
