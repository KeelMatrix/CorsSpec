using System.Net;
using System.Net.Http;
using KeelMatrix.CorsSpec;

namespace KeelMatrix.CorsSpec.Tests;

public sealed class RequestGenerationTests
{
    [Theory]
    [InlineData("/orders")]
    [InlineData("orders")]
    public void Valid_application_paths_create_relative_requests(string path)
    {
        var scenario = new CorsScenario(path, "https://app.example", HttpMethod.Get);
        using var request = CorsVerifier.CreateActualRequest(scenario);

        Assert.False(request.RequestUri!.IsAbsoluteUri);
    }

    [Fact]
    public async Task Simple_request_sends_origin_and_actual_method_without_preflight()
    {
        var handler = new RecordingHandler(request => ResponseFactory.Cors());
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://service.test") };
        var contract = new CorsContract(
            new CorsScenario("/orders", "https://app.example", HttpMethod.Get),
            CorsExpectation.Allowed());

        var result = await new CorsVerifier(client).VerifyAsync(contract);

        Assert.True(result.IsSuccess, result.Summary);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("/orders", request.RequestUri!.AbsolutePath);
        Assert.Equal("https://app.example", request.Headers.GetValues("Origin").Single());
        Assert.Empty(request.Headers.Accept);
    }

    [Theory]
    [InlineData("PATCH", false, false)]
    [InlineData("PATCH", true, false)]
    [InlineData("PATCH", false, true)]
    [InlineData("PATCH", true, true)]
    [InlineData("GET", true, false)]
    [InlineData("GET", true, true)]
    [InlineData("HEAD", true, false)]
    [InlineData("POST", true, true)]
    public async Task Every_preflight_carries_exactly_the_browser_accept_value(
        string method,
        bool includeRequestedHeader,
        bool useCredentials)
    {
        var handler = new RecordingHandler(request =>
        {
            if (request.Method == HttpMethod.Options)
            {
                var accept = request.Headers.Accept.ToArray();
                if (accept.Length != 1 || accept[0].MediaType != "*/*" || accept[0].Quality is not null)
                {
                    return new HttpResponseMessage(HttpStatusCode.NotAcceptable);
                }
            }

            return ResponseFactory.Cors(
                methods: method,
                headers: includeRequestedHeader ? "X-Trace" : null,
                credentials: useCredentials);
        });
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://service.test") };

        var result = await new CorsVerifier(client).VerifyAsync(new CorsContract(
            new CorsScenario(
                "/orders",
                "https://app.example",
                new HttpMethod(method),
                includeRequestedHeader ? new[] { "X-Trace" } : null,
                useCredentials),
            CorsExpectation.Allowed()));

        Assert.True(result.IsSuccess, result.Summary);
        var preflight = Assert.Single(handler.Requests, request => request.Method == HttpMethod.Options);
        var accept = Assert.Single(preflight.Headers.Accept);
        Assert.Equal("*/*", accept.MediaType);
        Assert.Null(accept.Quality);
    }

    [Fact]
    public async Task Non_simple_request_sends_preflight_with_normalized_metadata_then_actual_request()
    {
        var handler = new RecordingHandler(request => request.Method == HttpMethod.Options
            ? ResponseFactory.Cors(headers: "X-Trace, Authorization", maxAge: "600")
            : ResponseFactory.Cors(exposed: "X-Request-Id"));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://service.test") };
        var contract = new CorsContract(
            new CorsScenario("/orders", "https://app.example", HttpMethod.Delete, new[] { "X-Trace", "Authorization" }),
            CorsExpectation.Allowed(expectedExposedHeaders: new[] { "X-Request-Id" }, expectedMaxAge: TimeSpan.FromMinutes(10)));

        var result = await new CorsVerifier(client).VerifyAsync(contract);

        Assert.True(result.IsSuccess, result.Summary);
        Assert.Equal(2, handler.Requests.Count);
        var preflight = handler.Requests[0];
        Assert.Equal(HttpMethod.Options, preflight.Method);
        Assert.Equal("https://app.example", preflight.Headers.GetValues("Origin").Single());
        Assert.Equal("DELETE", preflight.Headers.GetValues("Access-Control-Request-Method").Single());
        Assert.Equal("authorization, x-trace", preflight.Headers.GetValues("Access-Control-Request-Headers").Single());
        Assert.Equal(HttpMethod.Delete, handler.Requests[1].Method);
    }

    [Fact]
    public async Task Denied_preflight_does_not_send_actual_request()
    {
        var handler = new RecordingHandler(_ => ResponseFactory.Cors(methods: "GET"));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://service.test") };
        var contract = new CorsContract(
            new CorsScenario("/orders", "https://untrusted.example", HttpMethod.Delete),
            CorsExpectation.Denied());

        var result = await new CorsVerifier(client).VerifyAsync(contract);

        Assert.True(result.IsSuccess, result.Summary);
        Assert.True(result.PreflightSent);
        Assert.False(result.ActualRequestSent);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Origin_is_metadata_only_and_is_never_used_as_destination()
    {
        var handler = new RecordingHandler(_ => ResponseFactory.Cors(origin: "https://does-not-exist.invalid"));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://service.test") };
        var contract = new CorsContract(
            new CorsScenario("/orders", "https://does-not-exist.invalid", HttpMethod.Get),
            CorsExpectation.Allowed());

        var result = await new CorsVerifier(client).VerifyAsync(contract);

        Assert.True(result.IsSuccess, result.Summary);
        Assert.Equal("https://service.test", handler.Requests.Single().RequestUri!.GetLeftPart(UriPartial.Authority));
    }

    [Theory]
    [InlineData("Accept", "text/html")]
    [InlineData("Accept", "text/html\u001f")]
    [InlineData("Accept-Language", "en-US, en;q=0.9")]
    [InlineData("Accept-Language", "en_US")]
    [InlineData("Content-Language", "en-US")]
    [InlineData("Content-Language", "en_US")]
    [InlineData("Content-Type", "text/plain")]
    [InlineData("Content-Type", "application/json")]
    [InlineData("Range", "bytes=0-99")]
    public async Task Value_sensitive_caller_headers_always_use_a_conservative_preflight(string header, string value)
    {
        var handler = new RecordingHandler(request => request.Method == HttpMethod.Options
            ? ResponseFactory.Cors(methods: "GET", headers: header)
            : ResponseFactory.Cors(vary: null));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://service.test") };
        client.DefaultRequestHeaders.TryAddWithoutValidation(header, value);

        var result = await new CorsVerifier(client).VerifyAsync(new CorsContract(
            new CorsScenario("/orders", "https://app.example", HttpMethod.Get, new[] { header }),
            CorsExpectation.Allowed()));

        Assert.True(result.IsSuccess, result.Summary);
        Assert.True(result.PreflightSent);
        Assert.Equal(HttpMethod.Options, handler.Requests[0].Method);
        Assert.Equal(2, handler.Requests.Count);
    }
}
