using System.Net;

namespace KeelMatrix.CorsSpec;

// This value belongs to the terminal boundary, before caller handlers regain control.
// Never retain a mutable response/request/header collection as verdict evidence.
internal sealed class FirstCorsResponse
{
    private static readonly string[] HeaderNames =
    [
        "Access-Control-Allow-Origin", "Access-Control-Allow-Methods", "Access-Control-Allow-Headers",
        "Access-Control-Allow-Credentials", "Access-Control-Expose-Headers", "Access-Control-Max-Age", "Vary"
    ];
    private readonly KeyValuePair<string, string[]>[] _headers;

    private FirstCorsResponse(HttpResponseMessage response)
    {
        StatusCode = response.StatusCode;
        RequestMethod = response.RequestMessage?.Method.Method;
        RequestUri = response.RequestMessage?.RequestUri;
        _headers = HeaderNames
            .Where(name => response.Headers.NonValidated.Contains(name))
            .Select(name => new KeyValuePair<string, string[]>(name, response.Headers.NonValidated[name].ToArray()))
            .ToArray();
    }

    internal HttpStatusCode StatusCode { get; }
    internal string? RequestMethod { get; }
    internal Uri? RequestUri { get; }

    internal static FirstCorsResponse Capture(HttpResponseMessage response) => new(response);

    internal HttpResponseMessage CreateEvaluationResponse()
    {
        var response = new HttpResponseMessage(StatusCode);
        foreach (var (name, values) in _headers)
        {
            response.Headers.TryAddWithoutValidation(name, values);
        }
        return response;
    }
}
