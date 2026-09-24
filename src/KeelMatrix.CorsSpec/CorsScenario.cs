using System.Net.Http;

namespace KeelMatrix.CorsSpec;

/// <summary>Describes one browser-style CORS request scenario.</summary>
public sealed class CorsScenario
{
    /// <summary>Creates a scenario for a relative application path.</summary>
    /// <param name="path">The application path sent through the supplied <see cref="HttpClient"/>.</param>
    /// <param name="origin">The origin metadata placed on the request; it is never contacted.</param>
    /// <param name="method">The actual request method.</param>
    /// <param name="requestedHeaders">Headers that a browser would request permission to send.</param>
    /// <param name="useCredentials">Whether the browser contract expects credentialed CORS permission.</param>
    public CorsScenario(
        string path,
        string origin,
        HttpMethod method,
        IEnumerable<string>? requestedHeaders = null,
        bool useCredentials = false)
    {
        Path = Validation.RequirePath(path);
        Origin = Validation.RequireOrigin(origin);
        Method = method ?? throw new ArgumentNullException(nameof(method));
        if (string.IsNullOrWhiteSpace(method.Method) || method.Method.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException("The HTTP method must be a valid token.", nameof(method));
        }

        RequestedHeaders = Validation.NormalizeHeaderNames(requestedHeaders, nameof(requestedHeaders));
        UseCredentials = useCredentials;
    }

    /// <summary>Gets the relative application path.</summary>
    public string Path { get; }

    /// <summary>Gets the request origin metadata.</summary>
    public string Origin { get; }

    /// <summary>Gets the actual request method.</summary>
    public HttpMethod Method { get; }

    /// <summary>Gets the normalized names of headers requested by the preflight.</summary>
    public IReadOnlyList<string> RequestedHeaders { get; }

    /// <summary>Gets a value indicating whether the contract expects credentialed access.</summary>
    public bool UseCredentials { get; }

    internal bool RequiresPreflight => !IsSimpleMethod(Method) || RequestedHeaders.Any(static header => !IsSafelistedRequestHeader(header));

    internal static bool IsSimpleMethod(HttpMethod method) =>
        method == HttpMethod.Get || method == HttpMethod.Head || method == HttpMethod.Post;

    private static bool IsSafelistedRequestHeader(string name) =>
        name.Equals("Accept", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Accept-Language", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Content-Language", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Range", StringComparison.OrdinalIgnoreCase);
}
