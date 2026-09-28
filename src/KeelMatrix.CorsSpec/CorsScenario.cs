using System.Net.Http;

namespace KeelMatrix.CorsSpec;

/// <summary>Describes one browser-style CORS request scenario.</summary>
public sealed class CorsScenario
{
    /// <summary>Creates a scenario for a relative application path.</summary>
    /// <param name="path">The application path sent through the supplied <see cref="HttpClient"/>.</param>
    /// <param name="origin">The origin metadata placed on the request; it is never contacted.</param>
    /// <param name="method">The actual request method. Browser-standard names are normalized to uppercase; custom method casing is preserved for exact allow-method matching.</param>
    /// <param name="requestedHeaders">Header names that a browser would request permission to send when their values are supplied by the caller outside this scenario. Unknown values conservatively force a preflight; simple safelisted names remain simple when their values are not modeled.</param>
    /// <param name="useCredentials">Whether the browser contract expects credentialed CORS permission.</param>
    /// <param name="requestHeaders">Header names and values to serialize on the actual request. Values let the verifier classify value-sensitive safelisted headers.</param>
    public CorsScenario(
        string path,
        string origin,
        HttpMethod method,
        IEnumerable<string>? requestedHeaders = null,
        bool useCredentials = false,
        IEnumerable<CorsRequestHeader>? requestHeaders = null)
    {
        Path = Validation.RequirePath(path);
        Origin = Validation.RequireOrigin(origin);
        Method = Validation.RequireMethod(method);

        var declaredNames = Validation.NormalizeHeaderNames(requestedHeaders, nameof(requestedHeaders));
        RequestHeaders = Validation.NormalizeRequestHeaders(requestHeaders, nameof(requestHeaders));
        RequestedHeaders = Validation.MergeHeaderNames(declaredNames, RequestHeaders.Select(static header => header.Name));
        UseCredentials = useCredentials;
    }

    /// <summary>Gets the relative application path.</summary>
    public string Path { get; }

    /// <summary>Gets the request origin metadata.</summary>
    public string Origin { get; }

    /// <summary>Gets the actual request method. Browser-standard names use their uppercase wire representation; custom method casing is preserved for exact allow-method matching.</summary>
    public HttpMethod Method { get; }

    /// <summary>Gets the normalized names declared for caller-supplied request headers.</summary>
    public IReadOnlyList<string> RequestedHeaders { get; }

    /// <summary>Gets header values serialized on the actual request.</summary>
    public IReadOnlyList<CorsRequestHeader> RequestHeaders { get; }

    /// <summary>Gets a value indicating whether the contract expects credentialed access.</summary>
    public bool UseCredentials { get; }

    internal bool RequiresPreflight(IReadOnlyCollection<string> effectivePreflightHeaderNames) =>
        !IsSimpleMethod(Method) || effectivePreflightHeaderNames.Count != 0;

    internal static bool IsNameSafelistedWithoutValue(string name) =>
        name.Equals("Accept", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Accept-Language", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Content-Language", StringComparison.OrdinalIgnoreCase);

    internal static bool IsSimpleMethod(HttpMethod method) =>
        method == HttpMethod.Get || method == HttpMethod.Head || method == HttpMethod.Post;

}
