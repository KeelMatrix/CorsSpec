using System.Net;
using KeelMatrix.CorsSpec;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// The same consumer contract runs against source and the isolated built package.
internal static class ClientBoundaryProbes
{
    private const string Origin = "https://app.example";
    private static readonly string[] CallerHeaderNames = ["Cookie", "Authorization", "X-Test", "X-Default", "X-Factory"];

    internal static async Task RunAsync()
    {
        var localState = new HostState();
        using var server = new TestServer(new WebHostBuilder().Configure(app => app.Run(localState.Respond)));
        using var factory = new BoundaryFactory();
        var networkState = new HostState();
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
        await using var network = builder.Build();
        network.Run(networkState.Respond);
        await network.StartAsync();
        var networkAddress = new Uri(network.Urls.Single());
        var checks = 0;

        foreach (var transport in new[] { "TestServer", "WebApplicationFactory", "HttpClientHandler", "SocketsHttpHandler" })
        {
            var state = transport == "TestServer" ? localState : transport == "WebApplicationFactory" ? factory.State : networkState;
            var address = transport is "TestServer" or "WebApplicationFactory" ? new Uri("http://localhost") : networkAddress;
            HttpMessageHandler Terminal() => transport switch
            {
                "TestServer" => server.CreateHandler(),
                "WebApplicationFactory" => factory.Server.CreateHandler(),
                "HttpClientHandler" => NetworkHandler(),
                _ => new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false, UseProxy = false }
            };

            foreach (var pipeline in new[] { "direct", "clean", "configured" })
            {
                foreach (var cleanFactory in pipeline == "configured" ? new[] { true } : new[] { false, true })
                {
                    foreach (var method in new[] { HttpMethod.Get, HttpMethod.Delete })
                    {
                        foreach (var allowed in new[] { false, true })
                        {
                            var configured = pipeline == "configured";
                            var handler = pipeline == "direct" ? null : new CallerHandler(Terminal(), configured);
                            using var client = CorsTransport.CreateClient(handler ?? Terminal());
                            client.BaseAddress = address;
                            client.Timeout = TimeSpan.FromSeconds(10);
                            if (configured) { client.DefaultRequestHeaders.Add("X-Default", "default"); }
                            var ownedHandlers = new List<CallerHandler>();
                            var verifier = cleanFactory
                                ? new CorsVerifier(client, () =>
                                {
                                    var owned = new CallerHandler(Terminal(), configured: false);
                                    ownedHandlers.Add(owned);
                                    return owned;
                                })
                                : new CorsVerifier(client);
                            using (var ordinary = await client.GetAsync("/ordinary")) { ordinary.EnsureSuccessStatusCode(); }
                            var before = state.Requests;
                            var configuredBefore = state.ConfiguredActuals;
                            var headers = configured ? new[] { "X-Test", "X-Default", "Authorization" } : Array.Empty<string>();
                            var result = await verifier.VerifyAsync(new CorsContract(
                                new CorsScenario(allowed ? "/allowed" : "/denied", Origin, method, headers, useCredentials: configured),
                                allowed ? CorsExpectation.Allowed() : CorsExpectation.Denied()));
                            var preflight = method == HttpMethod.Delete || configured;
                            Require(result.IsSuccess && result.PreflightSent == preflight && result.ActualRequestSent == (!preflight || allowed),
                                $"{transport}/{pipeline}/{cleanFactory}/{method}/{allowed}: {result.Summary}");
                            Require(state.Requests - before == (preflight && allowed ? 2 : 1), "Preparation reached the host or an operation was omitted.");
                            Require(state.ContaminatedPreflights == 0, "Caller defaults or credentials reached a clean preflight.");
                            Require(state.ConfiguredActuals - configuredBefore == (configured && result.ActualRequestSent ? 1 : 0), "Actual requests lost caller defaults or handler credentials.");
                            Require(ownedHandlers.Count == (cleanFactory && preflight ? 1 : 0) && ownedHandlers.All(owned => owned.Disposed), "Preflight handler ownership failed.");
                            Require(handler is null || !handler.Disposed, "Verification disposed the caller's handler.");
                            using (var ordinary = await client.GetAsync("/ordinary")) { ordinary.EnsureSuccessStatusCode(); }
                            client.Dispose();
                            Require(handler is null || handler.Disposed, "Caller disposal did not dispose its pipeline.");
                            checks++;
                        }
                    }
                }
            }
        }

        foreach (var kind in new[] { "TestServer", "factory-default", "factory-disabled", "new-network", "new-default", "opaque", "controlled-opaque" })
        {
            var opaque = new OpaqueHandler();
            using var client = kind switch
            {
                "TestServer" => server.CreateClient(),
                "factory-default" => factory.CreateClient(),
                "factory-disabled" => factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false }),
                "new-network" => new HttpClient(NetworkHandler()) { BaseAddress = networkAddress },
                "new-default" => new HttpClient { BaseAddress = networkAddress },
                "controlled-opaque" => CorsTransport.CreateClient(opaque),
                _ => new HttpClient(opaque)
            };
            client.BaseAddress ??= new Uri("http://localhost");
            foreach (var cleanFactory in new[] { false, true })
            {
                foreach (var method in new[] { HttpMethod.Get, HttpMethod.Delete })
                {
                    foreach (var allowed in new[] { false, true })
                    {
                        var before = localState.Requests + factory.State.Requests + networkState.Requests;
                        var factoryCalls = 0;
                        var verifier = cleanFactory ? new CorsVerifier(client, () => { factoryCalls++; return server.CreateHandler(); }) : new CorsVerifier(client);
                        var result = await verifier.VerifyAsync(new CorsContract(new CorsScenario("/allowed", Origin, method),
                            allowed ? CorsExpectation.Allowed() : CorsExpectation.Denied()));
                        Require(!result.IsSuccess && !result.PreflightSent && !result.ActualRequestSent &&
                            result.Issues.Any(issue => issue.Kind == CorsFailureKind.UnsupportedTransport), $"{kind}: {result.Summary}");
                        Require(result.Issues.Any(issue => issue.Message.Contains(kind == "controlled-opaque" ? "handler" : "CorsTransport.CreateClient", StringComparison.Ordinal)), "Unsupported result lacks setup guidance.");
                        Require(before == localState.Requests + factory.State.Requests + networkState.Requests && factoryCalls == 0 && opaque.Calls == 0, "An unsupported client contacted a host or invoked the factory.");
                        checks++;
                    }
                }
            }
        }

        // Factory defaults apply to CreateClient, but not to its direct Server handler.
        using var ordinaryFactoryClient = factory.CreateClient();
        using (var redirected = await ordinaryFactoryClient.GetAsync("/redirect"))
        {
            Require(redirected.StatusCode == HttpStatusCode.OK, "The factory redirect control did not follow its redirect.");
        }
        using var controlled = CorsTransport.CreateClient(factory.Server.CreateHandler());
        controlled.BaseAddress = new Uri("http://localhost");
        Require(!controlled.DefaultRequestHeaders.Contains("X-Factory"), "Factory ConfigureClient settings transferred unexpectedly.");
        using (var first = await controlled.GetAsync("/redirect")) { Require(first.StatusCode == HttpStatusCode.Found, "Direct factory handler followed a redirect."); }
        using (var cookie = await ordinaryFactoryClient.GetAsync("/cookie")) { cookie.EnsureSuccessStatusCode(); }
        using (var echo = await ordinaryFactoryClient.GetAsync("/echo")) { Require(echo.Headers.Contains("X-Cookie-Present"), "The factory cookie control did not retain its cookie."); }
        using (var cookie = await controlled.GetAsync("/cookie")) { cookie.EnsureSuccessStatusCode(); }
        using (var echo = await controlled.GetAsync("/echo")) { Require(!echo.Headers.Contains("X-Cookie-Present"), "Direct factory handler inherited cookie handling."); }
        Console.WriteLine($"Client boundary probes passed: {checks} supported/unsupported cases plus factory redirect, cookie and configuration controls.");
    }

    private static HttpClientHandler NetworkHandler() => new() { AllowAutoRedirect = false, UseCookies = false, UseProxy = false };
    private static void Require(bool condition, string message)
    {
        if (!condition) { throw new InvalidOperationException(message); }
    }

    private sealed class HostState
    {
        internal int Requests;
        internal int ContaminatedPreflights;
        internal int ConfiguredActuals;
        internal Task Respond(HttpContext context)
        {
            Interlocked.Increment(ref Requests);
            if (context.Request.Method == "OPTIONS" &&
                (context.Request.Headers.Accept.ToString() != "*/*" ||
                 CallerHeaderNames.Any(context.Request.Headers.ContainsKey)))
            {
                Interlocked.Increment(ref ContaminatedPreflights);
            }
            if (context.Request.Path == "/allowed")
            {
                context.Response.Headers["Access-Control-Allow-Origin"] = Origin;
                context.Response.Headers["Access-Control-Allow-Methods"] = "GET, DELETE";
                context.Response.Headers["Access-Control-Allow-Headers"] = "x-test,x-default,authorization";
                context.Response.Headers["Access-Control-Allow-Credentials"] = "true";
            }
            if (context.Request.Path == "/redirect")
            {
                context.Response.StatusCode = 302;
                context.Response.Headers.Location = "/ordinary";
            }
            if (context.Request.Path == "/cookie") { context.Response.Headers.SetCookie = "test=synthetic; Path=/"; }
            if (context.Request.Method != "OPTIONS" && (context.Request.Path == "/allowed" || context.Request.Path == "/denied") &&
                context.Request.Headers["X-Default"] == "default" && context.Request.Headers["X-Test"] == "configured" &&
                context.Request.Headers.Authorization == "Bearer synthetic" && context.Request.Headers.Cookie == "test=synthetic")
            {
                Interlocked.Increment(ref ConfiguredActuals);
            }
            if (context.Request.Path == "/echo" && context.Request.Headers.ContainsKey("Cookie")) { context.Response.Headers["X-Cookie-Present"] = "true"; }
            return Task.CompletedTask;
        }
    }

    private sealed class BoundaryFactory : WebApplicationFactory<BoundaryEntryPoint>
    {
        internal HostState State { get; } = new();
        protected override IHostBuilder CreateHostBuilder() => Host.CreateDefaultBuilder()
            .ConfigureLogging(logging => logging.ClearProviders())
            .ConfigureWebHostDefaults(web => web.Configure(app => app.Run(State.Respond)));
        protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseContentRoot(AppContext.BaseDirectory);
        protected override void ConfigureClient(HttpClient client)
        {
            base.ConfigureClient(client);
            client.DefaultRequestHeaders.Add("X-Factory", "factory");
        }
    }

    private sealed class BoundaryEntryPoint;

    private sealed class CallerHandler(HttpMessageHandler inner, bool configured) : DelegatingHandler(inner)
    {
        internal bool Disposed { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (configured)
            {
                request.Headers.Add("X-Test", "configured");
                request.Headers.Add("Authorization", "Bearer synthetic");
                request.Headers.Add("Cookie", "test=synthetic");
            }
            return base.SendAsync(request, cancellationToken);
        }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }

    private sealed class OpaqueHandler : HttpMessageHandler
    {
        internal int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request });
        }
    }
}
