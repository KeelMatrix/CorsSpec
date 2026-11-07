using System.Net;
using System.Runtime.CompilerServices;
using System.Text;

namespace KeelMatrix.CorsSpec;

/// <summary>Creates caller-owned clients with an observable, single-response transport boundary.</summary>
public static class CorsTransport
{
    private static readonly ConditionalWeakTable<HttpClient, ClientRegistration> Clients = new();

    /// <summary>Creates a client for CORS verification over a supported terminal handler.</summary>
    /// <param name="handler">A new handler pipeline ending in an exact <see cref="SocketsHttpHandler"/>, <see cref="HttpClientHandler"/>, or ASP.NET Core TestHost ClientHandler. Network handlers must disable automatic redirects, automatic authentication and automatic decompression. The client owns the pipeline.</param>
    /// <remarks>Create this client once and share it between integration tests and CorsVerifier. Configure its BaseAddress, defaults and timeout as usual. A direct WebApplicationFactory.Server.CreateHandler does not inherit factory client configuration or automatic redirect/cookie handlers. Verification first runs the handler pipeline without sending to the host to obtain effective headers. Handlers must support this preparation and send the same operation on execution. Verdicts evaluate the first response's status, CORS headers and request provenance captured by value before caller handlers regain control; later response mutations do not change that evidence. Unknown terminal handlers are unsupported. This version supplies no host-observer adapter.</remarks>
    public static HttpClient CreateClient(HttpMessageHandler handler) => CreateClient(handler, allowTestHandler: false);

    internal static HttpClient CreateClient(HttpMessageHandler handler, bool allowTestHandler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var pipeline = Wrap(handler, allowTestHandler);
        var client = new HttpClient(pipeline, disposeHandler: true);
        Register(client, allowTestHandler);
        return client;
    }

    internal static HttpMessageHandler Wrap(HttpMessageHandler handler, bool allowTestHandler)
    {
        var visited = new HashSet<HttpMessageHandler>(ReferenceEqualityComparer.Instance);
        var terminal = handler;
        DelegatingHandler? parent = null;
        while (terminal is DelegatingHandler delegating)
        {
            if (!visited.Add(terminal))
            {
                throw new ArgumentException("The handler pipeline contains a cycle.", nameof(handler));
            }
            parent = delegating;
            terminal = delegating.InnerHandler ?? throw new ArgumentException("The handler pipeline must have a terminal handler.", nameof(handler));
        }

        var boundary = new CorsSendBoundary(terminal, allowTestHandler);
        if (parent is null)
        {
            handler = boundary;
        }
        else
        {
            parent.InnerHandler = boundary;
        }

        return handler;
    }

    internal static void Register(HttpClient client, bool allowTestHandler) => Clients.Add(client, new ClientRegistration(allowTestHandler));

    private sealed record ClientRegistration(bool AllowTestHandler);

    internal static bool AllowsTestHandler(HttpClient client) => Clients.TryGetValue(client, out var registration) && registration.AllowTestHandler;

    internal static bool IsSupportedClient(HttpClient client) => Clients.TryGetValue(client, out _);
}

internal sealed class UnsupportedCorsRequestException(string message, CorsFailureKind kind = CorsFailureKind.UnsupportedRequest) : Exception(message)
{
    internal CorsFailureKind Kind { get; } = kind;
}

internal sealed class CorsSendContext(CorsScenario scenario, bool prepare, bool preflight = false) : IDisposable
{
    internal static readonly HttpRequestOptionsKey<CorsSendContext> Key = new("KeelMatrix.CorsSpec.SendContext");
    internal CorsScenario Scenario { get; } = scenario;
    internal bool Prepare { get; } = prepare;
    internal bool Preflight { get; } = preflight;
    internal int Calls;
    internal bool MaterializesHeaders { get; set; }
    internal Dictionary<string, Encoding> Encodings { get; } = new(StringComparer.OrdinalIgnoreCase);
    internal EffectiveCorsRequest? Effective { get; set; }
    internal string? Expected { get; init; }
    internal Uri? Target { get; init; }
    internal string? PreflightHeaders { get; set; }
    internal HttpResponseMessage? Response { get; set; }
    internal FirstCorsResponse? FirstResponse { get; set; }
    public void Dispose() => Response?.Dispose();
}

internal sealed class CorsSendBoundary : DelegatingHandler
{
    private readonly bool _allowTestHandler;
    private readonly HeaderEncodingSelector<HttpRequestMessage>? _originalSelector;
    private readonly HeaderEncodingSelector<HttpRequestMessage>? _controlledSelector;

    internal CorsSendBoundary(HttpMessageHandler handler, bool allowTestHandler) : base(handler)
    {
        _allowTestHandler = allowTestHandler;
        if (handler.GetType() == typeof(SocketsHttpHandler))
        {
            var sockets = (SocketsHttpHandler)handler;
            _originalSelector = sockets.RequestHeaderEncodingSelector;
            _controlledSelector = (name, request) =>
                request.Options.TryGetValue(CorsSendContext.Key, out var context)
                    ? context.Encodings.GetValueOrDefault(name, Encoding.ASCII)
                    : _originalSelector?.Invoke(name, request);
            sockets.RequestHeaderEncodingSelector = _controlledSelector;
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Ordinary caller traffic has no CORS verification context.
        if (!request.Options.TryGetValue(CorsSendContext.Key, out var context))
        {
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }

        if (Interlocked.Increment(ref context.Calls) != 1)
        {
            throw new UnsupportedCorsRequestException("A handler retried or redirected a CORS operation. Each operation must expose its first response.", CorsFailureKind.RedirectNotSupported);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (!string.Equals(request.Method.Method, context.Preflight ? "OPTIONS" : context.Scenario.Method.Method, StringComparison.Ordinal) ||
            context.Target is null || request.RequestUri != context.Target)
        {
            throw new UnsupportedCorsRequestException("A handler changed the scenario method or destination.");
        }
        var encoding = GetEncoding(request, context);
        var effective = EffectiveCorsRequest.Capture(request, context.Scenario, encoding, context.Preflight);
        if (context.MaterializesHeaders)
        {
            EnsureTestHostRepresentation(request.Headers);
            if (request.Content is not null)
            {
                EnsureTestHostRepresentation(request.Content.Headers);
            }
        }
        context.Effective = effective;
        if (context.PreflightHeaders is not null && context.PreflightHeaders != effective.HeaderValues)
        {
            throw new UnsupportedCorsRequestException("A handler changed generated preflight metadata.");
        }
        if (context.Expected is not null && context.Expected != effective.Identity)
        {
            throw new UnsupportedCorsRequestException("The handler pipeline changed the effective request between preparation and execution.");
        }

        if (context.Prepare)
        {
            context.Response = new HttpResponseMessage(HttpStatusCode.NoContent) { RequestMessage = request };
            return context.Response;
        }

        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        context.Response = response;
        context.FirstResponse = FirstCorsResponse.Capture(response);
        return response;
    }

    private Func<string, Encoding> GetEncoding(HttpRequestMessage request, CorsSendContext context)
    {
        if (InnerHandler!.GetType() == typeof(SocketsHttpHandler))
        {
            var sockets = (SocketsHttpHandler)InnerHandler!;
            if (sockets.AllowAutoRedirect || sockets.Credentials is not null || sockets.AutomaticDecompression != DecompressionMethods.None)
            {
                throw new UnsupportedCorsRequestException("SocketsHttpHandler must disable automatic redirects, authentication and decompression for CORS verification.", CorsFailureKind.RedirectNotSupported);
            }

            CheckCookies(sockets.UseCookies, sockets.CookieContainer, request, context);
            if (!ReferenceEquals(sockets.RequestHeaderEncodingSelector, _controlledSelector))
            {
                throw new UnsupportedCorsRequestException("The controlled request-header encoding selector was replaced.");
            }
            return name =>
            {
                if (!context.Encodings.TryGetValue(name, out var encoding))
                {
                    var selected = _originalSelector?.Invoke(name, request) ?? Encoding.ASCII;
                    if (selected.CodePage is not (20127 or 28591 or 65001))
                    {
                        throw new UnsupportedCorsRequestException("Only ASCII, Latin-1 and UTF-8 header encodings are supported.");
                    }
                    encoding = Encoding.GetEncoding(selected.CodePage);
                    context.Encodings.Add(name, encoding);
                }
                return encoding;
            };
        }

        if (InnerHandler!.GetType() == typeof(HttpClientHandler))
        {
            var clientHandler = (HttpClientHandler)InnerHandler!;
            if (clientHandler.AllowAutoRedirect || clientHandler.Credentials is not null || clientHandler.UseDefaultCredentials || clientHandler.AutomaticDecompression != DecompressionMethods.None)
            {
                throw new UnsupportedCorsRequestException("HttpClientHandler must disable automatic redirects, authentication and decompression for CORS verification.", CorsFailureKind.RedirectNotSupported);
            }

            CheckCookies(clientHandler.UseCookies, clientHandler.CookieContainer, request, context);
            return static _ => Encoding.ASCII;
        }

        var type = InnerHandler!.GetType();
        // An exact direct TestHost handler dispatches one request and returns its first response.
        // No ASP.NET Core dependency is introduced into the shipping library.
        var assembly = type.Assembly.GetName();
        if (_allowTestHandler ||
            (type.FullName == "Microsoft.AspNetCore.TestHost.ClientHandler" &&
             assembly.Name == "Microsoft.AspNetCore.TestHost" &&
             Convert.ToHexString(assembly.GetPublicKeyToken() ?? []).Equals("ADB9793829DDAE60", StringComparison.Ordinal)))
        {
            context.MaterializesHeaders = !_allowTestHandler;
            return static _ => Encoding.UTF8;
        }

        throw new UnsupportedCorsRequestException("The terminal handler cannot establish a single-response transport. Use a supported direct TestHost or no-auto-redirect network handler.", CorsFailureKind.UnsupportedTransport);
    }

    private static void EnsureTestHostRepresentation(System.Net.Http.Headers.HttpHeaders headers)
    {
        var expected = headers.NonValidated.ToDictionary(static header => header.Key,
            static header => string.Join(", ", header.Value), StringComparer.OrdinalIgnoreCase);
        // TestHost copies the parsed IEnumerable<string> values into host headers.
        // Its implicit formatter must not change the field observed by classification.
        foreach (var header in headers)
        {
            var values = header.Value.ToArray();
            if (values.Length != 1 || !expected.TryGetValue(header.Key, out var value) || values[0] != value)
            {
                throw new UnsupportedCorsRequestException("Direct TestHost would reformat or split an effective header. Supply a single canonical .NET header value or use a supported network transport.");
            }
        }
    }

    private static void CheckCookies(bool useCookies, CookieContainer cookies, HttpRequestMessage request, CorsSendContext context)
    {
        if (useCookies && request.RequestUri is { IsAbsoluteUri: true } uri &&
            (context.Preflight || !context.Scenario.UseCredentials) && cookies.GetCookieHeader(uri).Length != 0)
        {
            throw new UnsupportedCorsRequestException("The transport would send cookies on a preflight or a non-credentialed operation.");
        }
    }
}

internal sealed record EffectiveCorsRequest(string Identity, string HeaderValues, IReadOnlyList<string> PreflightNames)
{
    internal static EffectiveCorsRequest Capture(HttpRequestMessage request, CorsScenario scenario, Func<string, Encoding> encodingSelector, bool preflight)
    {
        var initialMethod = request.Method.Method;
        var initialUri = request.RequestUri;
        var initialContent = request.Content;
        var headers = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        CaptureHeaders(request.Headers);
        if (request.Content is not null)
        {
            CaptureHeaders(request.Content.Headers);
        }

        if (!headers.TryGetValue("Origin", out var origin) || origin != scenario.Origin)
        {
            throw new UnsupportedCorsRequestException("A handler removed or changed the scenario origin.");
        }

        var unsafeNames = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var safeNames = new List<string>();
        long safeBytes = 0;
        foreach (var (name, value) in headers)
        {
            var encoding = encodingSelector(name);
            if (encoding.CodePage is not (20127 or 28591 or 65001) ||
                (encoding.CodePage == 20127 && value.Any(static c => c > 127)) ||
                (encoding.CodePage == 28591 && value.Any(static c => c > 255)) ||
                value.Any(char.IsSurrogate))
            {
                throw new UnsupportedCorsRequestException("A request header is not representable by the supported ASCII, Latin-1 or UTF-8 transport encoding.");
            }

            var size = encoding.GetByteCount(value);
            if (name.Equals("Cookie", StringComparison.OrdinalIgnoreCase) || name.Equals("Cookie2", StringComparison.OrdinalIgnoreCase))
            {
                if (preflight || !scenario.UseCredentials)
                {
                    throw new UnsupportedCorsRequestException("Cookies require a credentialed actual request and cannot be sent on a preflight.");
                }
                continue;
            }

            if (name.Equals("Origin", StringComparison.OrdinalIgnoreCase))
            {
                if (value != scenario.Origin)
                {
                    throw new UnsupportedCorsRequestException("A handler changed the scenario origin.");
                }
                continue;
            }

            if (preflight)
            {
                if (name.Equals("Accept", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("Access-Control-Request-Method", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("Access-Control-Request-Headers", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                throw new UnsupportedCorsRequestException("A generated preflight acquired caller headers. Supply a clean preflight factory.");
            }

            // The empty body used to carry content headers may acquire this transport-owned field.
            if (name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase) && value == "0")
            {
                continue;
            }

            if (!scenario.RequestedHeaders.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                throw new UnsupportedCorsRequestException("The actual request contains an undeclared author header. Declare its name or value in the scenario.");
            }

            if (size > 128 || !Validation.IsCorsSafelistedRequestHeader(name, value, size))
            {
                unsafeNames.Add(name.ToLowerInvariant());
            }
            else
            {
                safeNames.Add(name.ToLowerInvariant());
                safeBytes += size;
            }
        }

        if (!preflight && scenario.RequestedHeaders.Any(name => !headers.ContainsKey(name)))
        {
            throw new UnsupportedCorsRequestException("A declared request header has no effective value. Supply CorsRequestHeader, a default, or a handler that supplies it during preparation.");
        }

        if (safeBytes > 1024)
        {
            unsafeNames.UnionWith(safeNames);
        }

        var identity = request.Method.Method + "\n" + request.RequestUri + "\n" +
            string.Join("\n", headers.Select(pair => pair.Key.ToLowerInvariant() + ":" + pair.Value + ":" + encodingSelector(pair.Key).CodePage));
        var headerValues = string.Join("\n", headers.Select(pair => pair.Key.ToLowerInvariant() + ":" + pair.Value));
        var afterSelection = request.Headers.NonValidated
            .Concat(request.Content?.Headers.NonValidated.ToArray() ?? [])
            .OrderBy(static header => header.Key, StringComparer.OrdinalIgnoreCase)
            .Select(static header => header.Key.ToLowerInvariant() + ":" + string.Join(", ", header.Value));
        if (initialMethod != request.Method.Method || initialUri != request.RequestUri || !ReferenceEquals(initialContent, request.Content) ||
            headerValues != string.Join("\n", afterSelection))
        {
            throw new UnsupportedCorsRequestException("The encoding selector changed the effective request. Selectors must not mutate request metadata.");
        }
        return new EffectiveCorsRequest(identity, headerValues, unsafeNames.ToArray());

        void CaptureHeaders(System.Net.Http.Headers.HttpHeaders collection)
        {
            foreach (var header in collection.NonValidated.ToArray())
            {
                var value = string.Join(", ", header.Value).Trim(' ', '\t');
                try
                {
                    Validation.RequireHeaderValue(value, nameof(request), allowHorizontalTab: true);
                }
                catch (ArgumentException)
                {
                    throw new UnsupportedCorsRequestException("An effective request header contains an invalid control character.");
                }
                headers.Add(header.Key, value);
                collection.Remove(header.Key);
                if (!collection.TryAddWithoutValidation(header.Key, value))
                {
                    throw new UnsupportedCorsRequestException("An effective request header could not be serialized.");
                }
            }
        }
    }
}
