using System.Net;
using System.Text;

namespace Kun.Sandbox.Tests.Infrastructure;

/// <summary>
/// Deterministic <see cref="HttpMessageHandler"/> used by
/// <see cref="Implementations.SupervisorSandboxProvider"/> tests.
/// Records every request and returns responses from a script keyed by
/// <c>{METHOD} {path}</c> or <c>{METHOD} {path-with-query}</c>.
/// </summary>
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Dictionary<string, Func<HttpRequestMessage, HttpResponseMessage>> _routes = new();

    public List<RecordedRequest> Requests { get; } = [];

    public StubHttpMessageHandler Map(HttpMethod method, string path, Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        _routes[$"{method.Method} {path}"] = handler;
        return this;
    }

    public StubHttpMessageHandler MapJson(HttpMethod method, string path, string jsonBody, HttpStatusCode status = HttpStatusCode.OK)
    {
        return Map(method, path, _ => new HttpResponseMessage(status)
        {
            Content = new StringContent(jsonBody, Encoding.UTF8, "application/json"),
        });
    }

    public StubHttpMessageHandler MapStatus(HttpMethod method, string path, HttpStatusCode status)
    {
        return Map(method, path, _ => new HttpResponseMessage(status));
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        Requests.Add(new RecordedRequest(request.Method, request.RequestUri!, body, request.Headers));

        var uri = request.RequestUri!;
        var keys = new[]
        {
            $"{request.Method.Method} {uri.AbsolutePath}{uri.Query}",
            $"{request.Method.Method} {uri.AbsolutePath}",
        };

        foreach (var key in keys)
        {
            if (_routes.TryGetValue(key, out var handler))
            {
                return handler(request);
            }
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent($"Unmapped route: {request.Method} {uri.PathAndQuery}", Encoding.UTF8, "text/plain"),
        };
    }
}

internal sealed record RecordedRequest(HttpMethod Method, Uri Uri, string Body, System.Net.Http.Headers.HttpRequestHeaders Headers);
