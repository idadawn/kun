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
    public override async ValueTask ApplyAsync(RequestTransformContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var user = context.HttpContext.User;

        // 未验证的 Authorization 不能穿过显式匿名路由进入下游。
        context.ProxyRequest.Headers.Remove("Authorization");

        // Only replace Authorization when a user principal is present
        if (user.Identity?.IsAuthenticated == true)
        {
            var token = await _issuer.IssueForAsync(user, context.HttpContext.RequestAborted)
                .ConfigureAwait(false);
            context.ProxyRequest.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
        }
    }
}
