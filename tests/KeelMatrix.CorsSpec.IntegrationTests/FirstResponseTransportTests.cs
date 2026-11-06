using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace KeelMatrix.CorsSpec.IntegrationTests;

public sealed class FirstResponseTransportTests
{
    public static IEnumerable<object[]> Redirects()
    {
        foreach (var status in new[] { 301, 302, 303, 307, 308 })
        {
            foreach (var kind in new[] { "self", "return", "same", "cross" })
            {
                foreach (var phase in new[] { "simple", "actual", "preflight" })
                {
                    foreach (var factory in new[] { false, true })
                    {
                        foreach (var allowed in new[] { false, true })
                        {
                            yield return [status, kind, phase, factory, allowed];
                        }
                    }
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(Redirects))]
    public async Task First_response_is_preserved_for_all_redirect_families(int status, string kind, string phase, bool factory, bool allowed)
    {
        await using var destination = await RedirectHost.StartAsync();
        await using var host = await RedirectHost.StartAsync(destination.BaseAddress);
        using var client = CorsTransport.CreateClient(NetworkHandler());
        client.BaseAddress = host.BaseAddress;
        var verifier = factory ? new CorsVerifier(client, NetworkHandler) : new CorsVerifier(client);
        var path = $"/redirect/{kind}/{status}?phase={phase}";
        var result = await verifier.VerifyAsync(new CorsContract(
            new CorsScenario(path, "https://app.example", phase == "simple" ? HttpMethod.Get : HttpMethod.Delete),
            allowed ? CorsExpectation.Allowed() : CorsExpectation.Denied()));

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Kind == CorsFailureKind.RedirectNotSupported);
        Assert.Equal(phase == "actual" ? 2 : 1, host.RequestCount);
        Assert.Equal(0, destination.RequestCount);
        Assert.Equal(0, host.TerminalCount);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Automatic_redirect_configuration_cannot_earn_a_verdict(bool factory, bool allowed)
    {
        await using var host = await RedirectHost.StartAsync();
        using var client = CorsTransport.CreateClient(new HttpClientHandler { AllowAutoRedirect = !factory, UseProxy = false });
        client.BaseAddress = host.BaseAddress;
        var verifier = factory
            ? new CorsVerifier(client, () => new HttpClientHandler { AllowAutoRedirect = true, UseProxy = false })
            : new CorsVerifier(client);
        var result = await verifier.VerifyAsync(new CorsContract(
            new CorsScenario("/redirect/self/302?phase=preflight", "https://app.example", HttpMethod.Delete),
            allowed ? CorsExpectation.Allowed() : CorsExpectation.Denied()));
        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Kind == CorsFailureKind.RedirectNotSupported);
        Assert.Equal(0, host.RequestCount);
    }

    [Fact]
    public async Task Opaque_client_is_unsupported_before_either_request_path()
    {
        await using var host = await RedirectHost.StartAsync();
        using var client = new HttpClient(NetworkHandler()) { BaseAddress = host.BaseAddress };
        foreach (var method in new[] { HttpMethod.Get, HttpMethod.Delete })
        {
            foreach (var allowed in new[] { false, true })
            {
                var result = await new CorsVerifier(client, NetworkHandler).VerifyAsync(new CorsContract(
                    new CorsScenario("/redirect/self/302", "https://app.example", method),
                    allowed ? CorsExpectation.Allowed() : CorsExpectation.Denied()));
                Assert.False(result.IsSuccess);
                Assert.Contains(result.Issues, issue => issue.Kind == CorsFailureKind.UnsupportedTransport);
            }
        }

        Assert.Equal(0, host.RequestCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Rejected_response_content_and_factory_handlers_are_disposed(bool preflight)
    {
        await using var host = await RedirectHost.StartAsync();
        var actualHandler = new TrackingHandler(NetworkHandler());
        TrackingHandler? factoryHandler = null;
        using var client = CorsTransport.CreateClient(actualHandler);
        client.BaseAddress = host.BaseAddress;
        var verifier = new CorsVerifier(client, () => factoryHandler = new TrackingHandler(NetworkHandler()));
        var result = await verifier.VerifyAsync(new CorsContract(
            new CorsScenario($"/redirect/self/302?phase={(preflight ? "preflight" : "simple")}", "https://app.example", preflight ? HttpMethod.Delete : HttpMethod.Get),
            CorsExpectation.Denied()));
        Assert.False(result.IsSuccess);
        var observed = preflight ? factoryHandler! : actualHandler;
        Assert.True(observed.Contents.Last().Disposed);
        Assert.Equal(preflight, observed.Disposed);
        Assert.False(actualHandler.Disposed);
        client.Dispose();
        Assert.True(actualHandler.Disposed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancellation_propagates_and_disposes_only_owned_factory_handlers(bool preflight)
    {
        await using var host = await RedirectHost.StartAsync();
        var actualHandler = new TrackingHandler(NetworkHandler());
        TrackingHandler? factoryHandler = null;
        using var client = CorsTransport.CreateClient(actualHandler);
        client.BaseAddress = host.BaseAddress;
        client.Timeout = Timeout.InfiniteTimeSpan;
        var verifier = new CorsVerifier(client, () => factoryHandler = new TrackingHandler(NetworkHandler()));
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => verifier.VerifyAsync(new CorsContract(
            new CorsScenario("/wait", "https://app.example", preflight ? HttpMethod.Delete : HttpMethod.Get), CorsExpectation.Denied()), cancel.Token));
        Assert.False(actualHandler.Disposed);
        if (preflight) { Assert.True(factoryHandler!.Disposed); }
    }

    private static HttpClientHandler NetworkHandler() => new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false, UseProxy = false };

    private sealed class TrackingHandler(HttpMessageHandler handler) : DelegatingHandler(handler)
    {
        internal bool Disposed { get; private set; }
        internal List<TrackingContent> Contents { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = await base.SendAsync(request, cancellationToken);
            var content = new TrackingContent(response.Content);
            response.Content = content;
            Contents.Add(content);
            return response;
        }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }

    private sealed class TrackingContent(HttpContent inner) : HttpContent
    {
        internal bool Disposed { get; private set; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => inner.CopyToAsync(stream);
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override void Dispose(bool disposing) { Disposed = true; inner.Dispose(); base.Dispose(disposing); }
    }
}

internal sealed class RedirectHost(WebApplication application, Uri baseAddress) : IAsyncDisposable
{
    internal Uri BaseAddress { get; } = baseAddress;
    internal int RequestCount;
    internal int TerminalCount;

    internal static async Task<RedirectHost> StartAsync(Uri? other = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
        var app = builder.Build();
        RedirectHost? host = null;
        app.Run(async context =>
        {
            Interlocked.Increment(ref host!.RequestCount);
            if (context.Request.Path == "/wait")
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, context.RequestAborted);
                return;
            }
            var path = context.Request.Path.Value!;
            var phase = context.Request.Query["phase"].ToString();
            if (context.Request.Method == "OPTIONS" && phase != "preflight")
            {
                context.Response.StatusCode = 204;
                context.Response.Headers["Access-Control-Allow-Origin"] = "https://app.example";
                context.Response.Headers["Access-Control-Allow-Methods"] = "DELETE";
                context.Response.Headers.Vary = "Origin";
                return;
            }
            if (path.StartsWith("/redirect/", StringComparison.Ordinal))
            {
                var parts = path.Split('/');
                context.Response.StatusCode = int.Parse(parts[3], System.Globalization.CultureInfo.InvariantCulture);
                context.Response.Headers.Location = parts[2] switch
                {
                    "self" => context.Request.Path + context.Request.QueryString,
                    "return" => "/hop?back=" + Uri.EscapeDataString(path + context.Request.QueryString),
                    "cross" => new Uri(other ?? host.BaseAddress, "/hop?back=" + Uri.EscapeDataString(new Uri(host.BaseAddress, path + context.Request.QueryString).ToString())).ToString(),
                    _ => "/final"
                };
                return;
            }
            if (path == "/hop")
            {
                context.Response.StatusCode = 307;
                context.Response.Headers.Location = context.Request.Query["back"];
                return;
            }
            Interlocked.Increment(ref host.TerminalCount);
            context.Response.Headers["Access-Control-Allow-Origin"] = "https://app.example";
            context.Response.Headers.Vary = "Origin";
            context.Response.StatusCode = 200;
        });
        await app.StartAsync();
        host = new RedirectHost(app, new Uri(app.Urls.Single()));
        return host;
    }

    public async ValueTask DisposeAsync() => await application.DisposeAsync();
}
