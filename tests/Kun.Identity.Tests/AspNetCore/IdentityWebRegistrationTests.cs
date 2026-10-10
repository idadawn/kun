using Kun.Identity.Abstractions;
using Kun.Identity.AspNetCore;
using Kun.Identity.DependencyInjection;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Kun.Identity.Tests.AspNetCore;

public sealed class IdentityWebRegistrationTests
{
    [Fact]
    public void Registration_IsIdempotent_AndHelpersResolveInValidatedScope()
    {
        var services = new ServiceCollection(); services.AddLogging();
        services.AddKunIdentity(builder => builder.AddCookieSession(options => options.UseInMemoryStore()));
        services.AddKunIdentityAspNetCore(); services.AddKunIdentityAspNetCore();
        services.AddDataProtection().UseEphemeralDataProtectionProvider();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        using var scope = provider.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<SignInHelper>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<OidcLoginService>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<OidcLogoutService>());
        Assert.Same(scope.ServiceProvider.GetRequiredService<KunCurrentUser>(), scope.ServiceProvider.GetRequiredService<IAuthenticatedUser>());
        Assert.Equal(KunIdentityAspNetCoreServiceCollectionExtensions.CookieScheme, provider.GetRequiredService<IOptions<AuthenticationOptions>>().Value.DefaultChallengeScheme);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Registration_PreservesHostDefaultScheme_InEitherOrder(bool first)
    {
        var services = new ServiceCollection(); services.AddLogging();
        if (first) services.AddAuthentication("host-owned");
        services.AddKunIdentityAspNetCore();
        if (!first) services.AddAuthentication("host-owned");
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<AuthenticationOptions>>().Value;
        Assert.Equal("host-owned", options.DefaultScheme);
        Assert.Null(options.DefaultAuthenticateScheme); Assert.Null(options.DefaultChallengeScheme);
    }
}
