using System.Net;
using System.Net.Http;

namespace KeelMatrix.CorsSpec.Tests;

internal sealed class RecordingHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;

    public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        : this(responseFactory, null)
    {
    }

    private RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory, List<HttpRequestMessage>? requests)
    {
        _responseFactory = responseFactory;
        Requests = requests ?? new List<HttpRequestMessage>();
    }

    public List<HttpRequestMessage> Requests { get; }

    public bool IsDisposed { get; private set; }

    public HttpMessageHandler CreateSibling() => new RecordingHandler(_responseFactory, Requests);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(CloneRequest(request));
        var response = _responseFactory(request);
        response.RequestMessage ??= request;
        return Task.FromResult(response);
    }

    protected override void Dispose(bool disposing)
    {
        IsDisposed = true;
        base.Dispose(disposing);
    }

    private static HttpRequestMessage CloneRequest(HttpRequestMessage source)
    {
        var clone = new HttpRequestMessage(source.Method, source.RequestUri);
        foreach (var header in source.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (source.Content is not null)
        {
            clone.Content = new ByteArrayContent(Array.Empty<byte>());
            foreach (var header in source.Content.Headers)
            {
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        return clone;
    }
}

internal sealed class BlockingHandler : HttpMessageHandler
{
    public bool IsDisposed { get; private set; }

    public int RequestCount { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        RequestCount++;
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        return new HttpResponseMessage(HttpStatusCode.NoContent);
    }

    protected override void Dispose(bool disposing)
    {
        IsDisposed = true;
        base.Dispose(disposing);
    }
}

internal sealed class FaultingHandler : HttpMessageHandler
{
    public bool IsDisposed { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromException<HttpResponseMessage>(new HttpRequestException("synthetic preflight failure"));

    protected override void Dispose(bool disposing)
    {
        IsDisposed = true;
        base.Dispose(disposing);
    }
}

internal sealed class HeaderAddingHandler : DelegatingHandler
{
    private readonly IReadOnlyList<(string Name, string Value)> _headers;

    public HeaderAddingHandler(HttpMessageHandler innerHandler, params (string Name, string Value)[] headers)
        : base(innerHandler)
    {
        _headers = headers;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        foreach (var (name, value) in _headers)
        {
            request.Headers.TryAddWithoutValidation(name, value);
        }

        return base.SendAsync(request, cancellationToken);
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

internal sealed class RecordingTelemetry : ICorsTelemetry
{
    public int ActivationCount { get; private set; }

    public void TrackActivation() => ActivationCount++;
}

internal sealed class ThrowingTelemetry : ICorsTelemetry
{
    public void TrackActivation() => throw new InvalidOperationException("synthetic telemetry delivery failure");
}
