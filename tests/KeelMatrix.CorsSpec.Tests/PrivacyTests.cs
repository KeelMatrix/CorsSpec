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
}
