using Kun.Core.Security;
using Kun.Identity.Abstractions;
using Kun.Identity.Abstractions.Session;
using Kun.Identity.AspNetCore.Internal;
using Kun.Identity.Configuration;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Kun.Identity.AspNetCore;

/// <summary>Registers the browser identity helpers without replacing the host's authentication scheme.</summary>
public static class KunIdentityAspNetCoreServiceCollectionExtensions
{
    /// <summary>Authentication scheme for the verified local cookie principal.</summary>
    public const string CookieScheme = "KunCookieAuth";
    private sealed class Registered;

    /// <summary>Adds protected OIDC challenges, atomic replay checks, sign-in helpers, current-user bindings and antiforgery.</summary>
    public static IServiceCollection AddKunIdentityAspNetCore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (services.Any(descriptor => descriptor.ServiceType == typeof(Registered))) return services;
        services.AddSingleton(new Registered());
        services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, KunCookieAuthenticationHandler>(CookieScheme, _ => { });
        services.PostConfigure<AuthenticationOptions>(options =>
        {
            if (options.DefaultScheme is null)
            {
                options.DefaultAuthenticateScheme ??= CookieScheme;
                options.DefaultChallengeScheme ??= CookieScheme;
            }
        });
        services.AddHttpContextAccessor(); services.TryAddSingleton(TimeProvider.System);
        services.AddDataProtection(); services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");
        services.AddOptions<KunCookieAuthOptions>();
        services.TryAddScoped<ISessionRegistry>(provider => provider.GetRequiredService<ISessionStore>() as ISessionRegistry
            ?? throw new InvalidOperationException("Browser identity requires an atomic ISessionRegistry implementation."));
        services.TryAddScoped<SignInHelper>(provider => new SignInHelper(provider.GetRequiredService<ISessionStore>(), provider.GetRequiredService<IOptions<KunCookieAuthOptions>>(), provider.GetRequiredService<TimeProvider>()));
        services.TryAddScoped<ChallengeHelper>(); services.TryAddScoped<OidcLoginService>(); services.TryAddScoped<OidcLogoutService>();
        services.TryAddScoped<KunCurrentUser>();
        services.TryAddScoped<IAuthenticatedUser>(provider => provider.GetRequiredService<KunCurrentUser>());
        services.TryAddScoped<ICurrentUser>(provider => provider.GetRequiredService<KunCurrentUser>());
        return services;
    }
}
