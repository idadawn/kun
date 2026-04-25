using System.Security.Claims;

namespace Kun.Gateway.Identity;

/// <summary>
/// Default <see cref="IS2sPrincipalResolver"/> that returns the authenticated external principal
/// unchanged. Consumers that do not need principal enrichment get this for free via
/// <c>AddKunGateway</c>; anyone wiring a custom resolver should register their implementation
/// with <c>services.AddSingleton&lt;IS2sPrincipalResolver, MyResolver&gt;()</c> before
/// <c>AddKunGateway</c> runs.
/// </summary>
public sealed class NoOpS2sPrincipalResolver : IS2sPrincipalResolver
{
    /// <summary>Shared instance — the resolver is stateless.</summary>
    public static NoOpS2sPrincipalResolver Instance { get; } = new();

    /// <inheritdoc/>
    public Task<ClaimsPrincipal> ResolveAsync(
        ClaimsPrincipal externalPrincipal,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(externalPrincipal);
        return Task.FromResult(externalPrincipal);
    }
}
