using Microsoft.AspNetCore.Http;

namespace Kun.Security.Http;

/// <summary>
/// A <see cref="DelegatingHandler"/> that copies the <c>Authorization</c> header
/// from the current incoming HTTP request to all outgoing requests made by the
/// <see cref="System.Net.Http.HttpClient"/> it is attached to.
/// This propagates the S2S JWT transparently across service boundaries.
/// </summary>
public sealed class PropagateS2sTokenHandler : DelegatingHandler
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    /// <summary>Initializes the handler with an HTTP context accessor.</summary>
    /// <param name="httpContextAccessor">Accessor for the current HTTP context.</param>
    public PropagateS2sTokenHandler(IHttpContextAccessor httpContextAccessor)
    {
        ArgumentNullException.ThrowIfNull(httpContextAccessor);
        _httpContextAccessor = httpContextAccessor;
    }

    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var incoming = _httpContextAccessor.HttpContext?.Request;
        if (incoming is not null
            && incoming.Headers.TryGetValue("Authorization", out var auth)
            && !string.IsNullOrEmpty(auth))
        {
            request.Headers.TryAddWithoutValidation("Authorization", (string?)auth);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
