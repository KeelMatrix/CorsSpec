using System.Net;
using System.Net.Http;
using KeelMatrix.CorsSpec;

namespace KeelMatrix.CorsSpec.Tests;

public sealed class HeaderInterpretationTests
{
    [Fact]
    public async Task A_success_status_does_not_hide_a_rejected_preflight_method()
    {
        var handler = new RecordingHandler(_ => ResponseFactory.Cors(methods: "GET"));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://service.test") };
        var contract = new CorsContract(
            new CorsScenario("/orders", "https://app.example", HttpMethod.Delete),
            CorsExpectation.Allowed());

        var result = await new CorsVerifier(client).VerifyAsync(contract);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Kind == CorsFailureKind.MethodRejected);
        Assert.Equal(HttpStatusCode.NoContent, result.PreflightStatusCode);
    }

    [Fact]
    public async Task Requested_header_rejection_is_distinct_from_method_rejection()
    {
        var handler = new RecordingHandler(_ => ResponseFactory.Cors(headers: "X-Other"));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://service.test") };
        var contract = new CorsContract(
            new CorsScenario("/orders", "https://app.example", HttpMethod.Delete, new[] { "X-Trace" }),
            CorsExpectation.Allowed());

        var result = await new CorsVerifier(client).VerifyAsync(contract);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Kind == CorsFailureKind.RequestedHeaderRejected);
        Assert.DoesNotContain(result.Issues, issue => issue.Kind == CorsFailureKind.MethodRejected);
    }

    [Fact]
    public async Task Credentialed_contract_requires_credentials_header_and_exact_origin()
    {
        var handler = new RecordingHandler(_ => ResponseFactory.Cors(origin: "*", credentials: true));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://service.test") };
        var contract = new CorsContract(
            new CorsScenario("/orders", "https://app.example", HttpMethod.Get, useCredentials: true),
            CorsExpectation.Allowed());

        var result = await new CorsVerifier(client).VerifyAsync(contract);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Kind == CorsFailureKind.MissingOrMismatchedAllowOrigin || issue.Kind == CorsFailureKind.CredentialsMismatch);
    }

    [Fact]
    public async Task Wildcard_is_accepted_only_when_explicitly_expected_for_non_credentialed_contract()
    {
        var handler = new RecordingHandler(_ => ResponseFactory.Cors(origin: "*", vary: null));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://service.test") };
        var contract = new CorsContract(
            new CorsScenario("/orders", "https://app.example", HttpMethod.Get),
            CorsExpectation.Allowed(allowWildcardOrigin: true, requireVaryOrigin: true));

        var result = await new CorsVerifier(client).VerifyAsync(contract);

        Assert.True(result.IsSuccess, result.Summary);
    }

    [Fact]
    public async Task Missing_vary_exposed_header_and_max_age_are_reported()
    {
        var handler = new RecordingHandler(request => request.Method == HttpMethod.Options
            ? ResponseFactory.Cors(vary: null, maxAge: "30")
            : ResponseFactory.Cors(vary: null, exposed: "X-Other"));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://service.test") };
        var contract = new CorsContract(
            new CorsScenario("/orders", "https://app.example", HttpMethod.Delete),
            CorsExpectation.Allowed(expectedExposedHeaders: new[] { "X-Request-Id" }, expectedMaxAge: TimeSpan.FromSeconds(60), requireVaryOrigin: true));

        var result = await new CorsVerifier(client).VerifyAsync(contract);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Kind == CorsFailureKind.MissingVaryOrigin);
        Assert.Contains(result.Issues, issue => issue.Kind == CorsFailureKind.ExposedHeadersMismatch);
        Assert.Contains(result.Issues, issue => issue.Kind == CorsFailureKind.MaxAgeMismatch);
    }

    [Fact]
    public async Task Denied_simple_request_passes_without_requiring_a_failure_status()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://service.test") };
        var contract = new CorsContract(
            new CorsScenario("/orders", "https://untrusted.example", HttpMethod.Get),
            CorsExpectation.Denied());

        var result = await new CorsVerifier(client).VerifyAsync(contract);

        Assert.True(result.IsSuccess, result.Summary);
        Assert.Equal(HttpStatusCode.OK, result.ActualStatusCode);
    }
}
