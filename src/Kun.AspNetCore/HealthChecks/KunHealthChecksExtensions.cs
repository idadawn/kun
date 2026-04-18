using Microsoft.Extensions.DependencyInjection;

namespace Kun.AspNetCore.HealthChecks;

/// <summary>Extension methods for registering Kun health checks.</summary>
public static class KunHealthChecksExtensions
{
    /// <summary>
    /// Registers the standard ASP.NET Core health check infrastructure.
    /// Infrastructure-specific checks (DB, Redis, RabbitMQ) are registered separately
    /// by the respective Kun infrastructure modules.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The <see cref="IHealthChecksBuilder"/> for further configuration.</returns>
    public static IHealthChecksBuilder AddKunHealthChecks(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services.AddHealthChecks();
    }
}
