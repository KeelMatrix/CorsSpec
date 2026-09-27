using System.Net.Http;
using KeelMatrix.CorsSpec;

namespace KeelMatrix.CorsSpec.Tests;

public sealed class PrivacyTests
{
    [Fact]
    public async Task Diagnostics_do_not_copy_response_body_or_authorization_values()
    {
        var handler = new RecordingHandler(_ =>
        {
            var response = ResponseFactory.Cors(origin: "https://other.example", vary: null);
            response.Content = new StringContent("secret response body");
            response.Headers.TryAddWithoutValidation("X-Diagnostic", "Bearer secret-token");
            return response;
        });
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://service.test") };
        var contract = new CorsContract(
            new CorsScenario("/orders", "https://app.example", HttpMethod.Get),
            CorsExpectation.Allowed());

        var result = await new CorsVerifier(client).VerifyAsync(contract);

        Assert.DoesNotContain("secret response body", result.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-token", result.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain("Bearer", result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Recording_handler_shows_caller_owned_request_boundary_without_sensitive_diagnostics()
    {
        var handler = new RecordingHandler(request =>
        {
            var response = ResponseFactory.Cors(origin: "https://app.example");
            response.Content = new StringContent("private response");
            response.Headers.TryAddWithoutValidation("X-Private", "private-response-header");
            return response;
        });
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://service.test") };
        var result = await new CorsVerifier(client).VerifyAsync(new CorsContract(
            new CorsScenario("/orders", "https://app.example", HttpMethod.Delete, new[] { "X-Trace" }),
            CorsExpectation.Allowed()));

        Assert.True(result.IsSuccess, result.Summary);
        Assert.Equal("/orders", handler.Requests[0].RequestUri!.AbsolutePath);
        Assert.Equal("https://app.example", handler.Requests[0].Headers.GetValues("Origin").Single());
        Assert.Equal("DELETE", handler.Requests[0].Headers.GetValues("Access-Control-Request-Method").Single());
        Assert.DoesNotContain("private-response", result.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain("private-response-header", result.Summary, StringComparison.Ordinal);
    }
}
