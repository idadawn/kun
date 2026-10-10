using Kun.Identity.Abstractions;
using Kun.Identity.Abstractions.Session;
using Microsoft.AspNetCore.Http;

namespace Kun.Identity.AspNetCore;

/// <summary>Completes the protected browser transaction before creating a local session.</summary>
public sealed class OidcLoginService(ChallengeHelper challenges, IEnumerable<IOidcClient> clients, SignInHelper signIn)
{
    /// <summary>Validates one-time state, exchanges with the original verifier/nonce/redirect URI, signs in, and returns a local redirect path.</summary>
    public async Task<string> CompleteAsync(HttpContext context, string code, string state, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        var transaction = await challenges.ConsumeAsync(context, state, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("OIDC browser transaction is invalid, expired or already consumed.");
        var client = clients.SingleOrDefault(value => value.Name == transaction.Client)
            ?? throw new InvalidOperationException("OIDC client is no longer registered.");
        var tokens = await client.ExchangeCodeAsync(new OidcCodeExchange(code, transaction.RedirectUri!, transaction.CodeVerifier, transaction.Nonce), cancellationToken).ConfigureAwait(false);
        await signIn.SignInAsync(context, tokens, cancellationToken).ConfigureAwait(false);
        return transaction.ReturnUrl;
    }
}

/// <summary>Validates provider logout instructions before atomically revoking local sessions.</summary>
public sealed class OidcLogoutService(IEnumerable<IOidcClient> clients, ISessionRegistry sessions)
{
    /// <summary>Processes a signed logout token for the explicitly selected provider; issuer/sid scope and replay protection are preserved.</summary>
    public async Task<int> HandleAsync(string clientName, string logoutToken, CancellationToken cancellationToken = default)
    {
        var client = clients.SingleOrDefault(value => value.Name == clientName) ?? throw new InvalidOperationException("OIDC client is not registered.");
        var logout = await client.ValidateLogoutTokenAsync(logoutToken, cancellationToken).ConfigureAwait(false);
        return await sessions.ApplyLogoutAsync(logout, cancellationToken).ConfigureAwait(false);
    }
}
