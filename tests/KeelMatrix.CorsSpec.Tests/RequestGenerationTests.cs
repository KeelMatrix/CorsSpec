using System.Net;
using System.Net.Http;
using KeelMatrix.CorsSpec;

namespace KeelMatrix.CorsSpec.Tests;

public sealed class RequestGenerationTests
{
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
}
