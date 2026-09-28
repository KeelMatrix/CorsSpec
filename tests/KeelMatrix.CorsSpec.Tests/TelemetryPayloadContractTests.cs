using System.Net.Http;
using KeelMatrix.CorsSpec;

namespace KeelMatrix.CorsSpec.Tests;

public sealed class TelemetryPayloadContractTests
{
    [Fact]
    public async Task Product_verdict_crosses_the_injected_telemetry_boundary_without_contract_data()
    {
        var telemetry = new RecordingTelemetry();
        var handler = new RecordingHandler(request => request.Method == HttpMethod.Options
            ? ResponseFactory.Cors(origin: "https://internal.example", methods: "DELETE", headers: "Authorization")
            : ResponseFactory.Cors(origin: "https://internal.example", vary: null));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://service.test") };

        var result = await new CorsVerifier(client, handler.CreateSibling, telemetry, () => false).VerifyAsync(new CorsContract(
            new CorsScenario(
                "/private/orders",
                "https://internal.example",
                HttpMethod.Delete,
                new[] { "Authorization" },
                requestHeaders: new[] { new CorsRequestHeader("Authorization", "Bearer synthetic-secret") }),
            CorsExpectation.Allowed()));

        Assert.True(result.IsSuccess, result.Summary);
        Assert.Equal(1, telemetry.ActivationCount);

        var preflight = Assert.Single(handler.Requests, request => request.Method == HttpMethod.Options);
        Assert.DoesNotContain("Bearer synthetic-secret", preflight.Headers.ToString(), StringComparison.Ordinal);
        var actual = Assert.Single(handler.Requests, request => request.Method == HttpMethod.Delete);
        Assert.Contains("Bearer synthetic-secret", actual.Headers.ToString(), StringComparison.Ordinal);
    }
}
