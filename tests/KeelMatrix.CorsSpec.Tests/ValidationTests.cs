using System.Net;
using System.Net.Http;
using KeelMatrix.CorsSpec;

namespace KeelMatrix.CorsSpec.Tests;

public sealed class ValidationTests
{
    [Fact]
    public async Task Contradictory_credentialed_wildcard_expectation_fails_before_io()
    {
        var handler = new RecordingHandler(_ => throw new InvalidOperationException("request should not execute"));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://service.test") };
        var contract = new CorsContract(
            new CorsScenario("/orders", "https://app.example", HttpMethod.Get, useCredentials: true),
            CorsExpectation.Allowed(allowWildcardOrigin: true));

        var result = await new CorsVerifier(client).VerifyAsync(contract);

        Assert.False(result.IsSuccess);
        Assert.False(result.PreflightSent);
        Assert.False(result.ActualRequestSent);
        Assert.Contains(result.Issues, issue => issue.Kind == CorsFailureKind.MalformedScenario);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public void Scenario_rejects_an_absolute_target_path()
    {
        Assert.Throws<ArgumentException>(() => new CorsScenario("https://service.test/orders", "https://app.example", HttpMethod.Get));
    }

    [Fact]
    public void Matrix_is_bounded()
    {
        var scenario = new CorsScenario("/orders", "https://app.example", HttpMethod.Get);
        var contracts = Enumerable.Repeat(new CorsContract(scenario, CorsExpectation.Denied()), 257);

        Assert.Throws<ArgumentException>(() => new CorsMatrix(contracts));
    }
}
