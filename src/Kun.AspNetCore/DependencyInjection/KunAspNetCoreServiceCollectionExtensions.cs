using Kun.AspNetCore.ExceptionHandling;
using Microsoft.Extensions.DependencyInjection;

namespace Kun.AspNetCore.DependencyInjection;

/// <summary>
/// Extension methods for registering Kun ASP.NET Core services.
/// </summary>
public static class KunAspNetCoreServiceCollectionExtensions
{
    /// <summary>
    /// Registers Kun ASP.NET Core services:
    /// <list type="bullet">
    ///   <item><see cref="KunExceptionHandler"/> via <c>AddExceptionHandler</c></item>
    ///   <item>Standard <c>ProblemDetails</c> support</item>
    /// </list>
    /// Call <c>app.UseExceptionHandler()</c> to activate the exception handler in the pipeline.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddKunAspNetCore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddExceptionHandler<KunExceptionHandler>();
        services.AddProblemDetails();

        return services;
    }
}
