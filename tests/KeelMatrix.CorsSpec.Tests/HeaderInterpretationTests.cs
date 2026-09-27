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

    [Theory]
    [InlineData(199, false)]
    [InlineData(200, true)]
    [InlineData(204, true)]
    [InlineData(299, true)]
    [InlineData(300, false)]
    [InlineData(302, false)]
    [InlineData(304, false)]
    [InlineData(307, false)]
    [InlineData(400, false)]
    [InlineData(407, false)]
    [InlineData(500, false)]
    public async Task Allowed_preflight_requires_a_2xx_status_and_never_sends_actual_on_failure(int statusCode, bool expectedSuccess)
    {
        var handler = new RecordingHandler(_ => ResponseFactory.Cors(statusCode: (HttpStatusCode)statusCode));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://service.test") };
        var contract = new CorsContract(
            new CorsScenario("/orders", "https://app.example", HttpMethod.Delete, new[] { "X-Trace" }),
            CorsExpectation.Allowed());

        var result = await new CorsVerifier(client).VerifyAsync(contract);

        Assert.Equal(expectedSuccess, result.IsSuccess);
        Assert.Equal(expectedSuccess, result.ActualRequestSent);
        if (!expectedSuccess)
        {
            Assert.Contains(result.Issues, issue => issue.Kind == CorsFailureKind.PreflightStatusRejected);
        }
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

    [Theory]
    [InlineData("true", true)]
    [InlineData("True", false)]
    [InlineData("TRUE", false)]
    [InlineData(" true", false)]
    [InlineData("true ", false)]
    [InlineData("false", false)]
    [InlineData(null, false)]
    public async Task Credential_permission_requires_one_exact_lowercase_true_value(string? credentials, bool expectedSuccess)
    {
        var handler = new RecordingHandler(_ =>
        {
            var response = ResponseFactory.Cors(origin: "https://app.example");
            if (credentials is not null)
            {
                response.Headers.TryAddWithoutValidation("Access-Control-Allow-Credentials", credentials);
            }

            return response;
        });
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://service.test") };
        var contract = new CorsContract(
            new CorsScenario("/orders", "https://app.example", HttpMethod.Get, useCredentials: true),
            CorsExpectation.Allowed());

        var result = await new CorsVerifier(client).VerifyAsync(contract);

        Assert.Equal(expectedSuccess, result.IsSuccess);
        if (!expectedSuccess)
        {
            Assert.Contains(result.Issues, issue => issue.Kind == CorsFailureKind.CredentialsMismatch);
        }
    }

    [Fact]
    public async Task Duplicate_credential_permission_values_are_rejected()
    {
        var handler = new RecordingHandler(_ =>
        {
            var response = ResponseFactory.Cors(origin: "https://app.example");
            response.Headers.TryAddWithoutValidation("Access-Control-Allow-Credentials", new[] { "true", "true" });
            return response;
        });
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://service.test") };
        var result = await new CorsVerifier(client).VerifyAsync(new CorsContract(
            new CorsScenario("/orders", "https://app.example", HttpMethod.Get, useCredentials: true),
            CorsExpectation.Allowed()));

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Kind == CorsFailureKind.CredentialsMismatch);
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
    public async Task Non_credentialed_wildcards_grant_methods_headers_and_exposed_headers()
    {
        var handler = new RecordingHandler(request => request.Method == HttpMethod.Options
            ? ResponseFactory.Cors(methods: "*", headers: "*", vary: null)
            : ResponseFactory.Cors(exposed: "*", vary: null));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://service.test") };
        var result = await new CorsVerifier(client).VerifyAsync(new CorsContract(
            new CorsScenario("/orders", "https://app.example", HttpMethod.Delete, new[] { "X-Trace" }),
            CorsExpectation.Allowed(expectedExposedHeaders: new[] { "X-Request-Id" })));

        Assert.True(result.IsSuccess, result.Summary);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Credentialed_wildcards_do_not_grant_preflight_method_or_headers()
    {
        var handler = new RecordingHandler(_ => ResponseFactory.Cors(origin: "https://app.example", methods: "*", headers: "*", credentials: true));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://service.test") };
        var result = await new CorsVerifier(client).VerifyAsync(new CorsContract(
            new CorsScenario("/orders", "https://app.example", HttpMethod.Delete, new[] { "X-Trace" }, useCredentials: true),
            CorsExpectation.Allowed()));

        Assert.False(result.IsSuccess);
        Assert.False(result.ActualRequestSent);
        Assert.Contains(result.Issues, issue => issue.Kind == CorsFailureKind.MethodRejected);
        Assert.Contains(result.Issues, issue => issue.Kind == CorsFailureKind.RequestedHeaderRejected);
    }

    [Fact]
    public async Task Wildcards_do_not_cover_authorization_or_set_cookie()
    {
        var authorizationHandler = new RecordingHandler(_ => ResponseFactory.Cors(headers: "*"));
        using var authorizationClient = new HttpClient(authorizationHandler) { BaseAddress = new Uri("https://service.test") };
        var authorizationResult = await new CorsVerifier(authorizationClient).VerifyAsync(new CorsContract(
            new CorsScenario("/orders", "https://app.example", HttpMethod.Delete, new[] { "Authorization" }),
            CorsExpectation.Allowed()));

        var exposeHandler = new RecordingHandler(_ => ResponseFactory.Cors(exposed: "*", vary: null));
        using var exposeClient = new HttpClient(exposeHandler) { BaseAddress = new Uri("https://service.test") };
        var exposeResult = await new CorsVerifier(exposeClient).VerifyAsync(new CorsContract(
            new CorsScenario("/orders", "https://app.example", HttpMethod.Get),
            CorsExpectation.Allowed(expectedExposedHeaders: new[] { "Set-Cookie" })));

        Assert.False(authorizationResult.IsSuccess);
        Assert.Contains(authorizationResult.Issues, issue => issue.Kind == CorsFailureKind.RequestedHeaderRejected);
        Assert.False(exposeResult.IsSuccess);
        Assert.Contains(exposeResult.Issues, issue => issue.Kind == CorsFailureKind.ExposedHeadersMismatch);
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
