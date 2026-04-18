using Yarp.ReverseProxy.Transforms;

namespace Kun.Gateway.Transforms;

/// <summary>
/// YARP request transform that replaces the outgoing <c>Authorization</c> header
/// with an internal S2S JWT minted by <see cref="S2sTokenIssuer"/>.
/// Runs on every proxied request after the external JWT has been validated.
/// </summary>
internal sealed class S2sAuthTransform : RequestTransform
{
    private readonly S2sTokenIssuer _issuer;

    internal S2sAuthTransform(S2sTokenIssuer issuer)
    {
        ArgumentNullException.ThrowIfNull(issuer);
        _issuer = issuer;
    }

    /// <inheritdoc />
    public override ValueTask ApplyAsync(RequestTransformContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var user = context.HttpContext.User;

        // Only replace Authorization when a user principal is present
        if (user.Identity?.IsAuthenticated == true)
        {
            var token = _issuer.IssueFor(user);
            context.ProxyRequest.Headers.Remove("Authorization");
            context.ProxyRequest.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
        }

        return ValueTask.CompletedTask;
    }
}
