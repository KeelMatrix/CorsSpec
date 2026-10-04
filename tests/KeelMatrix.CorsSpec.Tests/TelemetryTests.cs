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
    public async Task One_activation_is_requested_for_a_meaningful_matrix_execution()
    {
        var telemetry = new RecordingTelemetry();
        var handler = new RecordingHandler(_ => ResponseFactory.Cors(vary: null));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://service.test") };
        var contracts = new CorsMatrix(new[]
        {
            AllowedSimpleContract(),
            new CorsContract(
                new CorsScenario("/other", "https://untrusted.example", HttpMethod.Get),
                CorsExpectation.Denied())
        });

        var results = await new CorsVerifier(client, telemetry).VerifyMatrixAsync(contracts);

        Assert.Equal(2, results.Count);
        Assert.All(results, result => Assert.True(result.IsSuccess, result.Summary));
        Assert.Equal(1, telemetry.ActivationCount);
    }

    [Fact]
    public async Task Activation_signal_has_no_contract_data_fields()
    {
        var telemetry = new RecordingTelemetry();
        var handler = new RecordingHandler(_ => ResponseFactory.Cors(vary: null));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://service.test") };

        await new CorsVerifier(client, telemetry).VerifyAsync(new CorsContract(
            new CorsScenario("/private/orders", "https://internal.example", HttpMethod.Get, new[] { "X-Private" }),
            CorsExpectation.Allowed()));

        Assert.Equal(1, telemetry.ActivationCount);
    }

    private static CorsContract AllowedSimpleContract() => new(
        new CorsScenario("/orders", "https://app.example", HttpMethod.Get),
        CorsExpectation.Allowed());
}
