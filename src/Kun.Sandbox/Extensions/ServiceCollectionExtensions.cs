using System.Net.Http.Headers;
using Kun.Sandbox.Abstractions;
using Kun.Sandbox.Implementations;
using Microsoft.Extensions.DependencyInjection;

namespace Kun.Sandbox.Extensions;

/// <summary>
/// Service-collection extensions that register a Kun sandbox backend.
/// </summary>
public static class KunSandboxServiceCollectionExtensions
{
    /// <summary>
    /// Registers a single <see cref="ISandboxProvider"/> implementation
    /// according to the backend selected in <paramref name="configure"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Callback that picks exactly one backend via <see cref="KunSandboxOptions.UseSupervisor"/> / <see cref="KunSandboxOptions.UseE2BCloud"/> / <see cref="KunSandboxOptions.UseInMemory"/>.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no backend was selected.</exception>
    public static IServiceCollection AddKunSandbox(
        this IServiceCollection services,
        Action<KunSandboxOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new KunSandboxOptions();
        configure(options);

        switch (options.Backend)
        {
            case SandboxBackendKind.Supervisor:
                RegisterSupervisor(services, options);
                break;

            case SandboxBackendKind.E2BCloud:
                services.AddSingleton<ISandboxProvider>(
                    _ => new E2BCloudSandboxProvider(options.E2BApiKey!, options.E2BTeamId!));
                break;

            case SandboxBackendKind.InMemory:
                services.AddSingleton<ISandboxProvider>(
                    _ => new InMemorySandboxProvider(options.InMemoryRunScript));
                break;

            case SandboxBackendKind.Unset:
            default:
                throw new InvalidOperationException(
                    "AddKunSandbox requires a backend. Call UseSupervisor, UseE2BCloud, or UseInMemory inside the configure callback.");
        }

        return services;
    }

    private static void RegisterSupervisor(IServiceCollection services, KunSandboxOptions options)
    {
        var builder = services.AddHttpClient(SupervisorSandboxProvider.HttpClientName, client =>
        {
            client.BaseAddress = options.SupervisorBaseUrl;
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            options.SupervisorHttpClientConfigurator?.Invoke(client);
        });

        if (options.SupervisorClientCertificate is { } cert)
        {
            builder.ConfigurePrimaryHttpMessageHandler(() =>
            {
                var handler = new HttpClientHandler();
                handler.ClientCertificates.Add(cert);
                return handler;
            });
        }

        services.AddSingleton<ISandboxProvider>(sp =>
        {
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            var client = factory.CreateClient(SupervisorSandboxProvider.HttpClientName);
            return new SupervisorSandboxProvider(client);
        });
    }
}
