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
        using var client = new TestHttpClient(handler) { BaseAddress = new Uri("https://service.test") };

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
        using var client = new TestHttpClient(handler) { BaseAddress = new Uri("https://service.test") };

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
        using var client = new TestHttpClient(handler) { BaseAddress = new Uri("https://service.test") };
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
    public async Task Matrix_requests_one_activation_for_any_response_verdict()
    {
        var telemetry = new RecordingTelemetry();
        var handler = new RecordingHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/no-response" => throw new HttpRequestException("synthetic network failure"),
            "/failed-verdict" => ResponseFactory.Cors(origin: "https://other.example", vary: null),
            _ => ResponseFactory.Cors(vary: null)
        });
        using var client = new TestHttpClient(handler) { BaseAddress = new Uri("https://service.test") };
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
        Assert.Equal(1, telemetry.ActivationCount);
    }


    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4)]
    public async Task Matrix_activation_is_aggregate_for_zero_one_or_many_eligible_cells(int eligible)
    {
        var telemetry = new RecordingTelemetry();
        using var client = new TestHttpClient(new RecordingHandler(request =>
            request.RequestUri!.AbsolutePath == "/network" ? throw new HttpRequestException() : ResponseFactory.Cors()))
        { BaseAddress = new Uri("https://service.test") };
        var contracts = Enumerable.Range(0, 4).Select(index => new CorsContract(
            new CorsScenario(index < eligible ? "/verdict" : "/network", "https://app.example", HttpMethod.Get),
            index % 2 == 0 ? CorsExpectation.Allowed() : CorsExpectation.Denied()));

        var results = await new CorsVerifier(client, telemetry).VerifyMatrixAsync(new CorsMatrix(contracts));

        Assert.Equal(4, results.Count);
        Assert.Equal(eligible == 0 ? 0 : 1, telemetry.ActivationCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task Cancelled_matrix_does_not_request_activation_even_after_partial_verdicts(int beforeCancellation)
    {
        var telemetry = new RecordingTelemetry();
        using var cancellation = new CancellationTokenSource();
        var sent = 0;
        using var client = new TestHttpClient(new RecordingHandler(_ =>
        {
            if (sent++ == beforeCancellation)
            {
                cancellation.Cancel();
                throw new OperationCanceledException(cancellation.Token);
            }
            return ResponseFactory.Cors();
        }))
        { BaseAddress = new Uri("https://service.test") };
        var matrix = new CorsMatrix(Enumerable.Range(0, 3).Select(_ => AllowedSimpleContract()));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new CorsVerifier(client, telemetry).VerifyMatrixAsync(matrix, cancellation.Token));
        Assert.Equal(0, telemetry.ActivationCount);
    }


    [Fact]
    public async Task Granted_preflight_followed_by_no_actual_response_does_not_activate()
    {
        var telemetry = new RecordingTelemetry();
        using var client = new TestHttpClient(new RecordingHandler(request => request.Method == HttpMethod.Options
            ? ResponseFactory.Cors()
            : throw new HttpRequestException()))
        { BaseAddress = new Uri("https://service.test") };
        var contract = new CorsContract(new CorsScenario("/orders", "https://app.example", HttpMethod.Delete), CorsExpectation.Allowed());
        var results = await new CorsVerifier(client, telemetry).VerifyMatrixAsync(new CorsMatrix(new[] { contract }));
        Assert.False(results[0].IsSuccess);
        Assert.NotNull(results[0].PreflightStatusCode);
        Assert.Null(results[0].ActualStatusCode);
        Assert.Equal(0, telemetry.ActivationCount);
    }

    private static CorsContract AllowedSimpleContract() => new(
        new CorsScenario("/orders", "https://app.example", HttpMethod.Get),
        CorsExpectation.Allowed());
}
