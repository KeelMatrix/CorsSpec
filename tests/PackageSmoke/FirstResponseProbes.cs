using System.Net;
using KeelMatrix.CorsSpec;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Logging;

// Compiled both as integration tests and as a consumer of the actual NuGet package.
internal static class FirstResponseProbes
{
    private const string Origin = "https://app.example";
    private static readonly Dictionary<string, string> Permissions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Access-Control-Allow-Origin"] = Origin,
        ["Access-Control-Allow-Methods"] = "DELETE",
        ["Access-Control-Allow-Headers"] = "x-test",
        ["Access-Control-Allow-Credentials"] = "true",
        ["Access-Control-Expose-Headers"] = "X-Request-Id",
        ["Access-Control-Max-Age"] = "600",
        ["Vary"] = "Origin"
    };

    internal static async Task RunAsync()
    {
        using var server = new TestServer(new WebHostBuilder().Configure(app => app.Run(Respond)));
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
        await using var network = builder.Build();
        network.Run(Respond);
        await network.StartAsync();
        var checks = 0;
        foreach (var useNetwork in new[] { false, true })
        {
            foreach (var factory in new[] { false, true })
            {
                foreach (var allowed in new[] { false, true })
                {
                    foreach (var phase in new[] { "simple", "preflight", "actual" })
                    {
                        foreach (var (field, correct) in Permissions)
                        {
                            foreach (var change in new[] { "add", "remove", "replace", "unchanged" })
                            {
                                var initial = change == "add" ? "absent" : change == "replace" ? "invalid" : correct;
                                await CheckAsync(field, initial, 200, response =>
                                {
                                    if (change == "unchanged") { return; }
                                    response.Headers.Remove(field);
                                    if (change != "remove") { response.Headers.TryAddWithoutValidation(field, correct); }
                                }, $"{field}/{change}");
                            }
                        }

                        foreach (var (initial, replacement) in new[] { (302, 200), (301, 204), (307, 200), (308, 200), (200, 302), (401, 200), (500, 204), (204, 403), (304, 200) })
                        {
                            await CheckAsync("", "", initial, response => response.StatusCode = (HttpStatusCode)replacement, $"status {initial}->{replacement}");
                        }

                        foreach (var change in new[] { "replace", "remove", "method", "uri", "equivalent", "irrelevant", "dispose" })
                        {
                            await CheckAsync("", "", 200, response =>
                            {
                                switch (change)
                                {
                                    case "replace": response.RequestMessage = new HttpRequestMessage(HttpMethod.Patch, "http://elsewhere.invalid/other"); break;
                                    case "remove": response.RequestMessage = null; break;
                                    case "method": response.RequestMessage!.Method = HttpMethod.Patch; break;
                                    case "uri": response.RequestMessage!.RequestUri = new Uri("http://elsewhere.invalid/other"); break;
                                    case "equivalent": response.RequestMessage = new HttpRequestMessage(response.RequestMessage!.Method, response.RequestMessage.RequestUri); break;
                                    case "irrelevant": response.Headers.TryAddWithoutValidation("X-Unrelated", "irrelevant"); break;
                                    case "dispose": response.Dispose(); break;
                                }
                            }, $"provenance/{change}");
                        }

                        // Preserve malformed multi-value metadata rather than letting a handler repair it.
                        foreach (var field in Permissions.Keys)
                        {
                            await CheckAsync(field, "invalid|invalid", 200, response =>
                            {
                                response.Headers.Remove(field);
                                response.Headers.TryAddWithoutValidation(field, Permissions[field]);
                            }, $"multi-value/{field}");
                        }

                        async Task CheckAsync(string field, string initial, int status, Action<HttpResponseMessage> mutation, string description)
                        {
                            var path = $"/response?phase={phase}&field={Uri.EscapeDataString(field)}&initial={Uri.EscapeDataString(initial)}&status={status}";
                            var scenario = new CorsScenario(path, Origin, phase == "simple" ? HttpMethod.Get : HttpMethod.Delete,
                                requestHeaders: phase == "simple" ? [] : [new CorsRequestHeader("X-Test", "value")], useCredentials: true);
                            var contract = new CorsContract(scenario, allowed
                                ? CorsExpectation.Allowed(requireVaryOrigin: true, expectedExposedHeaders: ["X-Request-Id"], expectedMaxAge: phase == "simple" ? null : TimeSpan.FromSeconds(600))
                                : CorsExpectation.Denied());
                            using var baseline = CreateClient(null, null);
                            var expected = await VerifyAsync(baseline, null, contract);
                            var observed = new List<ObservingHandler>();
                            using (var client = CreateClient(mutation, observed))
                            {
                                var actual = await VerifyAsync(client, mutation, contract, observed);
                                if (Signature(expected) != Signature(actual))
                                {
                                    throw new InvalidOperationException($"First response changed: network={useNetwork}, factory={factory}, allowed={allowed}, phase={phase}, {description}; expected={Signature(expected)}, actual={Signature(actual)}");
                                }
                                if (observed.Any(handler => handler.Contents.Any(content => !content.Disposed)))
                                {
                                    throw new InvalidOperationException("A response content was not disposed after verification.");
                                }
                                if (observed[0].Disposed || observed.Skip(1).Any(handler => !handler.Disposed))
                                {
                                    throw new InvalidOperationException("Caller/factory handler ownership changed.");
                                }
                            }
                            if (observed.Any(handler => !handler.Disposed)) { throw new InvalidOperationException("Client did not dispose its pipeline."); }
                            checks++;
                        }

                        HttpMessageHandler Terminal() => useNetwork
                            ? new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false, UseProxy = false }
                            : server.CreateHandler();

                        HttpClient CreateClient(Action<HttpResponseMessage>? mutation, List<ObservingHandler>? observed)
                        {
                            var handler = new ObservingHandler(Terminal(), phase, mutation);
                            observed?.Add(handler);
                            var client = CorsTransport.CreateClient(handler);
                            client.BaseAddress = useNetwork ? new Uri(network.Urls.Single()) : new Uri("http://localhost");
                            return client;
                        }

                        Task<CorsVerificationResult> VerifyAsync(HttpClient client, Action<HttpResponseMessage>? mutation, CorsContract contract, List<ObservingHandler>? observed = null)
                        {
                            var verifier = factory ? new CorsVerifier(client, () =>
                            {
                                var handler = new ObservingHandler(Terminal(), phase, mutation);
                                observed?.Add(handler);
                                return handler;
                            }) : new CorsVerifier(client);
                            return verifier.VerifyAsync(contract);
                        }
                    }
                }
            }
        }

        foreach (var useNetwork in new[] { false, true })
        {
            foreach (var preflight in new[] { false, true })
            {
                foreach (var cancel in new[] { false, true })
                {
                    foreach (var factory in new[] { false, true })
                    {
                        foreach (var allowed in new[] { false, true })
                        {
                            using var cancellation = new CancellationTokenSource();
                            var observed = new List<ObservingHandler>();
                            HttpMessageHandler Handler()
                            {
                                var handler = new ObservingHandler(useNetwork
                                    ? new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false, UseProxy = false }
                                    : server.CreateHandler(), preflight ? "preflight" : "simple", _ =>
                                    {
                                        if (cancel) { cancellation.Cancel(); cancellation.Token.ThrowIfCancellationRequested(); }
                                        throw new HttpRequestException("Synthetic failure after the first response.");
                                    });
                                observed.Add(handler);
                                return handler;
                            }
                            using (var client = CorsTransport.CreateClient(Handler()))
                            {
                                client.BaseAddress = useNetwork ? new Uri(network.Urls.Single()) : new Uri("http://localhost");
                                var verifier = factory ? new CorsVerifier(client, Handler) : new CorsVerifier(client);
                                var contract = new CorsContract(new CorsScenario("/response", Origin, preflight ? HttpMethod.Delete : HttpMethod.Get), allowed ? CorsExpectation.Allowed() : CorsExpectation.Denied());
                                try
                                {
                                    var result = await verifier.VerifyAsync(contract, cancellation.Token);
                                    if (cancel || !result.Issues.Any(issue => issue.Kind == CorsFailureKind.NetworkFailure)) { throw new InvalidOperationException("Post-response failure was not propagated."); }
                                }
                                catch (OperationCanceledException) when (cancel && cancellation.IsCancellationRequested) { }
                                if (observed.Any(handler => handler.Contents.Any(content => !content.Disposed)) || observed[0].Disposed || observed.Skip(1).Any(handler => !handler.Disposed))
                                {
                                    throw new InvalidOperationException("Post-response cancellation/failure leaked owned content or changed handler ownership.");
                                }
                            }
                            checks++;
                        }
                    }
                }
            }
        }

        Console.WriteLine($"First-response package probes passed: {checks} mutation/control/lifecycle cases over direct TestHost and loopback network.");
    }

    private static string Signature(CorsVerificationResult result) =>
        $"{result.IsSuccess}/{result.PreflightSent}/{result.PreflightStatusCode}/{result.ActualRequestSent}/{result.ActualStatusCode}/{string.Join(',', result.Issues.Select(issue => issue.Kind))}";

    private static Task Respond(HttpContext context)
    {
        context.Response.Headers["X-Response-Probe"] = "host";
        foreach (var (name, value) in Permissions) { context.Response.Headers[name] = value; }
        var phase = context.Request.Query["phase"].ToString();
        if ((context.Request.Method == "OPTIONS") == (phase == "preflight"))
        {
            var field = context.Request.Query["field"].ToString();
            if (field.Length != 0)
            {
                var initial = context.Request.Query["initial"].ToString();
                context.Response.Headers.Remove(field);
                if (initial != "absent") { context.Response.Headers[field] = initial.Split('|'); }
            }
            if (int.TryParse(context.Request.Query["status"], out var status)) { context.Response.StatusCode = status; }
        }
        return Task.CompletedTask;
    }

    private sealed class ObservingHandler(HttpMessageHandler inner, string phase, Action<HttpResponseMessage>? mutation) : DelegatingHandler(inner)
    {
        internal bool Disposed { get; private set; }
        internal List<TrackingContent> Contents { get; } = [];
        private readonly List<HttpRequestMessage> _replacements = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = await base.SendAsync(request, cancellationToken);
            if (response.Headers.Contains("X-Response-Probe"))
            {
                var content = new TrackingContent(response.Content);
                response.Content = content;
                Contents.Add(content);
                if ((request.Method == HttpMethod.Options) == (phase == "preflight"))
                {
                    mutation?.Invoke(response);
                    if (response.RequestMessage is { } replacement && !ReferenceEquals(request, replacement)) { _replacements.Add(replacement); }
                }
            }
            return response;
        }
        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            foreach (var replacement in _replacements) { replacement.Dispose(); }
            base.Dispose(disposing);
        }
    }

    private sealed class TrackingContent(HttpContent inner) : HttpContent
    {
        internal bool Disposed { get; private set; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => inner.CopyToAsync(stream);
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override void Dispose(bool disposing) { Disposed = true; inner.Dispose(); base.Dispose(disposing); }
    }
}
