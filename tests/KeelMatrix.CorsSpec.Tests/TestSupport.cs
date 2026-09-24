using System.Net;
using System.Net.Http;

namespace KeelMatrix.CorsSpec.Tests;

internal sealed class RecordingHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;

    public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
    {
        _responseFactory = responseFactory;
    }

    public List<HttpRequestMessage> Requests { get; } = new();

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(CloneRequest(request));
        return Task.FromResult(_responseFactory(request));
    }

    private static HttpRequestMessage CloneRequest(HttpRequestMessage source)
    {
        var clone = new HttpRequestMessage(source.Method, source.RequestUri);
        foreach (var header in source.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        return clone;
    }
}

internal static class ResponseFactory
{
    public static HttpResponseMessage Cors(
        string? origin = "https://app.example",
        string? methods = "DELETE",
        string? headers = "X-Trace",
        bool credentials = false,
        string? exposed = null,
        string? maxAge = null,
        string? vary = "Origin",
        HttpStatusCode statusCode = HttpStatusCode.NoContent)
    {
        var response = new HttpResponseMessage(statusCode);
        Add(response, "Access-Control-Allow-Origin", origin);
        Add(response, "Access-Control-Allow-Methods", methods);
        Add(response, "Access-Control-Allow-Headers", headers);
        Add(response, "Access-Control-Allow-Credentials", credentials ? "true" : null);
        Add(response, "Access-Control-Expose-Headers", exposed);
        Add(response, "Access-Control-Max-Age", maxAge);
        Add(response, "Vary", vary);
        return response;
    }

    private static void Add(HttpResponseMessage response, string name, string? value)
    {
        if (value is not null)
        {
            response.Headers.TryAddWithoutValidation(name, value);
        }
    }
}
