using Kun.Security.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Kun.Security.Extensions;

/// <summary>Extension methods on <see cref="IHttpClientBuilder"/> for Kun S2S auth propagation.</summary>
public static class HttpClientBuilderExtensions
{
    /// <summary>
    /// Chains <see cref="PropagateS2sTokenHandler"/> onto the HTTP client pipeline,
    /// so the S2S JWT from the current incoming request is forwarded to downstream services.
    /// </summary>
    /// <param name="builder">The HTTP client builder.</param>
    /// <returns>The builder for chaining.</returns>
    public static IHttpClientBuilder AddKunServiceAuth(this IHttpClientBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddTransient<PropagateS2sTokenHandler>();
        return builder.AddHttpMessageHandler<PropagateS2sTokenHandler>();
    }
}
