using System.Net;
using System.Net.Http;
using KeelMatrix.CorsSpec;

namespace KeelMatrix.CorsSpec.Tests;

public sealed class RedirectTests
{
    [Theory]
    [InlineData(301, "/same-target")]
    [InlineData(302, "/changed-method")]
    [InlineData(303, "https://other.test/orders")]
    [InlineData(307, "/same-target")]
    [InlineData(308, "https://other.test/orders")]
    public async Task Actual_redirect_responses_are_explicitly_unsupported(int statusCode, string location)
    {
        var handler = new RecordingHandler(_ =>
        {
            var response = new HttpResponseMessage((HttpStatusCode)statusCode);
            response.Headers.Location = new Uri(location, UriKind.RelativeOrAbsolute);
            return response;
        });
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://service.test") };

        var result = await new CorsVerifier(client).VerifyAsync(new CorsContract(
            new CorsScenario("/orders", "https://app.example", HttpMethod.Get),
            CorsExpectation.Allowed()));

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Kind == CorsFailureKind.RedirectNotSupported);
        Assert.True(result.ActualRequestSent);
    }

    [Theory]
    [InlineData(301)]
    [InlineData(302)]
    [InlineData(303)]
    [InlineData(307)]
    [InlineData(308)]
    public async Task Preflight_redirects_are_explicitly_unsupported(int statusCode)
    {
        var handler = new RecordingHandler(request =>
        {
            if (request.Method == HttpMethod.Options)
            {
                var response = new HttpResponseMessage((HttpStatusCode)statusCode);
                response.Headers.Location = new Uri("https://other.test/orders");
                return response;
            }

            throw new InvalidOperationException("actual request must not run");
        });
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://service.test") };

        var result = await new CorsVerifier(client, handler.CreateSibling).VerifyAsync(new CorsContract(
            new CorsScenario("/orders", "https://app.example", HttpMethod.Delete),
            CorsExpectation.Allowed()));

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Kind == CorsFailureKind.RedirectNotSupported);
        Assert.False(result.ActualRequestSent);
    }

    [Fact]
    public async Task A_final_response_with_changed_request_uri_is_not_accepted_as_the_original_operation()
    {
        var handler = new RecordingHandler(_ =>
        {
            var response = ResponseFactory.Cors(vary: null);
            response.RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://other.test/orders");
            return response;
        });
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://service.test") };

        var result = await new CorsVerifier(client).VerifyAsync(new CorsContract(
            new CorsScenario("/orders", "https://app.example", HttpMethod.Get),
            CorsExpectation.Allowed()));

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Kind == CorsFailureKind.RedirectNotSupported);
    }
}
