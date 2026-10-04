using System.Net.Http;
using KeelMatrix.CorsSpec;

namespace KeelMatrix.CorsSpec.Tests;

public sealed class TelemetryTests
{
    [Fact]
    public async Task Contract_without_a_response_verdict_does_not_emit_activation()
    {
        var telemetry = new RecordingTelemetry();
        var handler = new RecordingHandler(_ => throw new HttpRequestException("synthetic network failure"));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://service.test") };

        var result = await new CorsVerifier(client, telemetry).VerifyAsync(AllowedSimpleContract());

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Kind == CorsFailureKind.NetworkFailure);
        Assert.Equal(0, telemetry.ActivationCount);
    }

    [Fact]
    public async Task Failed_response_verdict_still_requests_one_activation()
    {
        var telemetry = new RecordingTelemetry();
        var handler = new RecordingHandler(_ => ResponseFactory.Cors(origin: "https://other.example", vary: null));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://service.test") };

        var result = await new CorsVerifier(client, telemetry).VerifyAsync(AllowedSimpleContract());

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Kind == CorsFailureKind.MissingOrMismatchedAllowOrigin);
        Assert.Equal(1, telemetry.ActivationCount);
    }

    [Fact]
    public async Task Invalid_contract_does_not_emit_activation()
    {
        var telemetry = new RecordingTelemetry();
        var handler = new RecordingHandler(_ => ResponseFactory.Cors(vary: null));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://service.test") };
        var contract = new CorsContract(
            new CorsScenario("/orders", "https://app.example", HttpMethod.Get, useCredentials: true),
            CorsExpectation.Allowed(allowWildcardOrigin: true));

        var result = await new CorsVerifier(client, telemetry).VerifyAsync(contract);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Kind == CorsFailureKind.MalformedScenario);
        Assert.Empty(handler.Requests);
        Assert.Equal(0, telemetry.ActivationCount);
    }

    [Fact]
    public async Task Matrix_requests_activation_only_for_contracts_with_response_verdicts()
    {
        var telemetry = new RecordingTelemetry();
        var handler = new RecordingHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/no-response" => throw new HttpRequestException("synthetic network failure"),
            "/failed-verdict" => ResponseFactory.Cors(origin: "https://other.example", vary: null),
            _ => ResponseFactory.Cors(vary: null)
        });
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://service.test") };
        var contracts = new CorsMatrix(new[]
        {
            AllowedSimpleContract(),
            new CorsContract(new CorsScenario("/no-response", "https://app.example", HttpMethod.Get), CorsExpectation.Allowed()),
            new CorsContract(new CorsScenario("/failed-verdict", "https://app.example", HttpMethod.Get), CorsExpectation.Allowed())
        });

        var results = await new CorsVerifier(client, telemetry).VerifyMatrixAsync(contracts);

        Assert.Equal(3, results.Count);
        Assert.True(results[0].IsSuccess, results[0].Summary);
        Assert.Contains(results[1].Issues, issue => issue.Kind == CorsFailureKind.NetworkFailure);
        Assert.Contains(results[2].Issues, issue => issue.Kind == CorsFailureKind.MissingOrMismatchedAllowOrigin);
        Assert.Equal(2, telemetry.ActivationCount);
    }

    private static CorsContract AllowedSimpleContract() => new(
        new CorsScenario("/orders", "https://app.example", HttpMethod.Get),
        CorsExpectation.Allowed());
}
