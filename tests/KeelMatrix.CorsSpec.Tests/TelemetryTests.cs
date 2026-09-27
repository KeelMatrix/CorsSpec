using System.Net.Http;
using KeelMatrix.CorsSpec;

namespace KeelMatrix.CorsSpec.Tests;

public sealed class TelemetryTests
{
    [Fact]
    public async Task Suppression_prevents_activation_event()
    {
        var telemetry = new RecordingTelemetry();
        var handler = new RecordingHandler(_ => ResponseFactory.Cors(vary: null));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://service.test") };

        var result = await new CorsVerifier(client, telemetry, () => TelemetryHost.IsSuppressed(static _ => "1"))
            .VerifyAsync(AllowedSimpleContract());

        Assert.True(result.IsSuccess, result.Summary);
        Assert.Equal(0, telemetry.ActivationCount);
    }

    [Fact]
    public async Task Contract_without_a_response_verdict_does_not_emit_activation()
    {
        var telemetry = new RecordingTelemetry();
        var handler = new RecordingHandler(_ => throw new HttpRequestException("synthetic network failure"));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://service.test") };

        var result = await new CorsVerifier(client, telemetry, () => false).VerifyAsync(AllowedSimpleContract());

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Kind == CorsFailureKind.NetworkFailure);
        Assert.Equal(0, telemetry.ActivationCount);
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

        var result = await new CorsVerifier(client, telemetry, () => false).VerifyAsync(contract);

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

        var results = await new CorsVerifier(client, telemetry, () => false).VerifyMatrixAsync(contracts);

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

        await new CorsVerifier(client, telemetry, () => false).VerifyAsync(new CorsContract(
            new CorsScenario("/private/orders", "https://internal.example", HttpMethod.Get, new[] { "X-Private" }),
            CorsExpectation.Allowed()));

        Assert.Equal(1, telemetry.ActivationCount);
    }

    [Fact]
    public async Task Telemetry_delivery_failure_does_not_change_the_verification_result()
    {
        var handler = new RecordingHandler(_ => ResponseFactory.Cors(vary: null));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://service.test") };
        var contract = AllowedSimpleContract();

        var expected = await new CorsVerifier(client, new RecordingTelemetry(), () => false).VerifyAsync(contract);
        var actual = await new CorsVerifier(client, new ThrowingTelemetry(), () => false).VerifyAsync(contract);

        Assert.Equal(expected.IsSuccess, actual.IsSuccess);
        Assert.Equal(expected.Summary, actual.Summary);
        Assert.Equal(expected.ActualStatusCode, actual.ActualStatusCode);
    }

    private static CorsContract AllowedSimpleContract() => new(
        new CorsScenario("/orders", "https://app.example", HttpMethod.Get),
        CorsExpectation.Allowed());
}
