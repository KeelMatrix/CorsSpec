using System.Net;
using System.Net.Http;
using KeelMatrix.CorsSpec;

namespace KeelMatrix.CorsSpec.Tests;

public sealed class HeaderInterpretationTests
{
    [Theory]
    [InlineData(" https://app.example ", true)]
    [InlineData("\thttps://app.example\t", true)]
    [InlineData("\u00a0https://app.example\u00a0", false)]
    [InlineData("\u2003https://app.example\u2003", false)]
    [InlineData("\u0000https://app.example\u0000", false)]
    [InlineData("\rhttps://app.example\r", false)]
    [InlineData("\nhttps://app.example\n", false)]
    [InlineData("\u0085https://app.example\u0085", false)]
    public async Task Exact_origin_accepts_only_http_ows_wrapping(string responseOrigin, bool expectedSuccess)
    {
        var handler = new RecordingHandler(_ => ResponseFactory.Cors(origin: responseOrigin, vary: "Origin"));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://service.test") };

        var result = await new CorsVerifier(client).VerifyAsync(new CorsContract(
            new CorsScenario("/orders", "https://app.example", HttpMethod.Get),
            CorsExpectation.Allowed(requireVaryOrigin: true)));

        Assert.Equal(expectedSuccess, result.IsSuccess);
    }

    [Theory]
    [InlineData(" * ", true)]
    [InlineData("\t*\t", true)]
    [InlineData("\u00a0*\u00a0", false)]
    [InlineData("\u2003*\u2003", false)]
    [InlineData("\u0000*\u0000", false)]
    [InlineData("\r*\r", false)]
    [InlineData("\n*\n", false)]
    [InlineData("\u0085*\u0085", false)]
    public async Task Wildcard_origin_accepts_only_http_ows_wrapping(string responseOrigin, bool expectedSuccess)
    {
        var handler = new RecordingHandler(_ => ResponseFactory.Cors(origin: responseOrigin, vary: null));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://service.test") };

        var result = await new CorsVerifier(client).VerifyAsync(new CorsContract(
            new CorsScenario("/orders", "https://app.example", HttpMethod.Get),
            CorsExpectation.Allowed(allowWildcardOrigin: true)));

        Assert.Equal(expectedSuccess, result.IsSuccess);
    }

    [Theory]
    [InlineData("\u00a0https://app.example\u00a0")]
    [InlineData("\rhttps://app.example\r")]
    [InlineData("\u0085https://app.example\u0085")]
    public async Task Credentialed_exact_origin_rejects_non_ows_boundaries(string responseOrigin)
    {
        var handler = new RecordingHandler(_ => ResponseFactory.Cors(
            origin: responseOrigin,
            credentials: true,
            vary: "Origin"));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://service.test") };

        var result = await new CorsVerifier(client).VerifyAsync(new CorsContract(
            new CorsScenario("/orders", "https://app.example", HttpMethod.Get, useCredentials: true),
            CorsExpectation.Allowed(requireVaryOrigin: true)));

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Kind is CorsFailureKind.MissingOrMismatchedAllowOrigin or CorsFailureKind.CredentialsMismatch);
    }

    [Theory]
    [InlineData(" Origin ", true)]
    [InlineData("\tOrigin\t", true)]
    [InlineData("\u00a0Origin\u00a0", false)]
    [InlineData("Origin,\u00a0", false)]
    [InlineData("Origin,\u0000Other", false)]
    [InlineData("Origin,\u0085Other", false)]
    public async Task Vary_origin_requires_a_valid_http_token_list(string vary, bool expectedSuccess)
    {
        var handler = new RecordingHandler(_ => ResponseFactory.Cors(vary: vary));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://service.test") };

        var result = await new CorsVerifier(client).VerifyAsync(new CorsContract(
            new CorsScenario("/orders", "https://app.example", HttpMethod.Get),
            CorsExpectation.Allowed(requireVaryOrigin: true)));

        Assert.Equal(expectedSuccess, result.IsSuccess);
    }

    [Theory]
    [InlineData("\u00a0https://app.example\u00a0")]
    [InlineData("\u2003https://app.example\u2003")]
    [InlineData("\u0000https://app.example\u0000")]
    [InlineData("\u0085https://app.example\u0085")]
    public async Task Malformed_preflight_origin_never_sends_the_actual_request(string responseOrigin)
    {
        var handler = new RecordingHandler(_ => ResponseFactory.Cors(origin: responseOrigin, vary: "Origin"));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://service.test") };

        var result = await new CorsVerifier(client).VerifyAsync(new CorsContract(
            new CorsScenario("/orders", "https://app.example", HttpMethod.Delete),
            CorsExpectation.Allowed(requireVaryOrigin: true)));

        Assert.False(result.IsSuccess);
        Assert.True(result.PreflightSent);
        Assert.False(result.ActualRequestSent);
        Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Options, handler.Requests[0].Method);
    }

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
    [InlineData("pAtCh", "pAtCh", true)]
    [InlineData("pAtCh", "PATCH", false)]
    [InlineData("pAtCh", "patch", false)]
    [InlineData("pAtCh", "PaTcH", false)]
    [InlineData("x-CuStOm", "x-CuStOm", true)]
    [InlineData("x-CuStOm", "X-CUSTOM", false)]
    public async Task Custom_method_allow_list_matching_requires_exact_casing(
        string requestedMethod,
        string allowedMethod,
        bool expectedSuccess)
    {
        var handler = new RecordingHandler(_ => ResponseFactory.Cors(methods: allowedMethod, headers: null));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://service.test") };

        var result = await new CorsVerifier(client).VerifyAsync(new CorsContract(
            new CorsScenario("/orders", "https://app.example", new HttpMethod(requestedMethod)),
            CorsExpectation.Allowed()));

        Assert.Equal(expectedSuccess, result.IsSuccess);
        if (expectedSuccess)
        {
            Assert.True(result.ActualRequestSent);
        }
        else
        {
            Assert.Contains(result.Issues, issue => issue.Kind == CorsFailureKind.MethodRejected);
            Assert.False(result.ActualRequestSent);
        }
    }

    [Theory]
    [InlineData("GET", false, null)]
    [InlineData("HEAD", false, null)]
    [InlineData("POST", false, null)]
    [InlineData("GET", true, null)]
    [InlineData("HEAD", true, null)]
    [InlineData("POST", true, null)]
    [InlineData("GET", false, "DELETE")]
    [InlineData("HEAD", false, "DELETE")]
    [InlineData("POST", false, "DELETE")]
    [InlineData("GET", true, "DELETE")]
    [InlineData("HEAD", true, "DELETE")]
    [InlineData("POST", true, "DELETE")]
    public async Task Safelisted_methods_do_not_require_a_matching_allow_methods_entry(
        string method,
        bool useCredentials,
        string? allowMethods)
    {
        var handler = new RecordingHandler(_ => ResponseFactory.Cors(
            methods: allowMethods,
            headers: "X-Trace",
            credentials: useCredentials));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://service.test") };

        var result = await new CorsVerifier(client).VerifyAsync(new CorsContract(
            new CorsScenario("/orders", "https://app.example", new HttpMethod(method), new[] { "X-Trace" }, useCredentials),
            CorsExpectation.Allowed()));

        Assert.True(result.IsSuccess, result.Summary);
        Assert.True(result.PreflightSent);
        Assert.True(result.ActualRequestSent);
    }

    [Theory]
    [InlineData("GET", "DELETE,\u001f")]
    [InlineData("HEAD", "DE LETE")]
    [InlineData("POST", "DELETE,\u001f")]
    public async Task Malformed_present_allow_methods_still_fail_for_safelisted_methods(string method, string allowMethods)
    {
        var handler = new RecordingHandler(_ => ResponseFactory.Cors(methods: allowMethods, headers: "X-Trace"));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://service.test") };

        var result = await new CorsVerifier(client).VerifyAsync(new CorsContract(
            new CorsScenario("/orders", "https://app.example", new HttpMethod(method), new[] { "X-Trace" }),
            CorsExpectation.Allowed()));

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Kind == CorsFailureKind.MethodRejected);
        Assert.False(result.ActualRequestSent);
    }

    [Fact]
    public async Task Non_safelisted_method_still_requires_an_exact_allow_methods_entry()
    {
        var handler = new RecordingHandler(_ => ResponseFactory.Cors(methods: null, headers: null));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://service.test") };

        var result = await new CorsVerifier(client).VerifyAsync(new CorsContract(
            new CorsScenario("/orders", "https://app.example", HttpMethod.Delete),
            CorsExpectation.Allowed()));

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Kind == CorsFailureKind.MethodRejected);
        Assert.False(result.ActualRequestSent);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Safelisted_method_with_requested_header_respects_credentialed_header_wildcard_rules(
        bool useCredentials,
        bool expectedSuccess)
    {
        var handler = new RecordingHandler(_ => ResponseFactory.Cors(
            methods: null,
            headers: "*",
            credentials: useCredentials));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://service.test") };

        var result = await new CorsVerifier(client).VerifyAsync(new CorsContract(
            new CorsScenario("/orders", "https://app.example", HttpMethod.Get, new[] { "X-Trace" }, useCredentials),
            CorsExpectation.Allowed()));

        Assert.Equal(expectedSuccess, result.IsSuccess);
        if (!expectedSuccess)
        {
            Assert.Contains(result.Issues, issue => issue.Kind == CorsFailureKind.RequestedHeaderRejected);
            Assert.False(result.ActualRequestSent);
        }
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
            var expectedFailure = statusCode is 300 or 302 or 307
                ? CorsFailureKind.RedirectNotSupported
                : CorsFailureKind.PreflightStatusRejected;
            Assert.Contains(result.Issues, issue => issue.Kind == expectedFailure);
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
    [InlineData(" true", true)]
    [InlineData("true ", true)]
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

    [Theory]
    [InlineData("Cache-Control")]
    [InlineData("Content-Language")]
    [InlineData("Content-Length")]
    [InlineData("Content-Type")]
    [InlineData("Expires")]
    [InlineData("Last-Modified")]
    [InlineData("Pragma")]
    public async Task Cors_safelisted_response_headers_are_exposed_without_an_explicit_grant(string header)
    {
        var handler = new RecordingHandler(_ => ResponseFactory.Cors(vary: null));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://service.test") };
        var result = await new CorsVerifier(client).VerifyAsync(new CorsContract(
            new CorsScenario("/orders", "https://app.example", HttpMethod.Get),
            CorsExpectation.Allowed(expectedExposedHeaders: new[] { header })));

        Assert.True(result.IsSuccess, result.Summary);
    }

    [Theory]
    [InlineData("Cache-Control")]
    [InlineData("Content-Language")]
    [InlineData("Content-Length")]
    [InlineData("Content-Type")]
    [InlineData("Expires")]
    [InlineData("Last-Modified")]
    [InlineData("Pragma")]
    public async Task Cors_safelisted_response_headers_remain_exposed_for_credentialed_requests(string header)
    {
        var handler = new RecordingHandler(_ => ResponseFactory.Cors(
            credentials: true,
            vary: null));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://service.test") };
        var result = await new CorsVerifier(client).VerifyAsync(new CorsContract(
            new CorsScenario("/orders", "https://app.example", HttpMethod.Get, useCredentials: true),
            CorsExpectation.Allowed(expectedExposedHeaders: new[] { header })));

        Assert.True(result.IsSuccess, result.Summary);
    }

    [Fact]
    public async Task Explicit_expose_tokens_accept_case_ows_and_duplicate_header_lines()
    {
        var handler = new RecordingHandler(_ =>
        {
            var response = ResponseFactory.Cors(vary: null);
            response.Headers.TryAddWithoutValidation("Access-Control-Expose-Headers", new[] { " X-Other ", " x-request-id " });
            return response;
        });
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://service.test") };
        var result = await new CorsVerifier(client).VerifyAsync(new CorsContract(
            new CorsScenario("/orders", "https://app.example", HttpMethod.Get),
            CorsExpectation.Allowed(expectedExposedHeaders: new[] { "X-Request-Id" })));

        Assert.True(result.IsSuccess, result.Summary);
    }

    [Theory]
    [InlineData("X-Request-Id", false, "")]
    [InlineData("X-Request-Id", false, "X-Request-Id")]
    [InlineData("X-Request-Id", true, "*")]
    [InlineData("X-Request-Id", true, "X-Request-Id")]
    [InlineData("X-Request-Id", false, "*")]
    public async Task Exposed_header_matrix_keeps_wildcard_credentials_and_forbidden_boundaries(
        string expectedHeader,
        bool useCredentials,
        string exposed)
    {
        var actualExposed = exposed.Length == 0 ? null : exposed;
        var handler = new RecordingHandler(_ => ResponseFactory.Cors(
            origin: "https://app.example",
            credentials: useCredentials,
            exposed: actualExposed,
            vary: null));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://service.test") };
        var result = await new CorsVerifier(client).VerifyAsync(new CorsContract(
            new CorsScenario("/orders", "https://app.example", HttpMethod.Get, useCredentials: useCredentials),
            CorsExpectation.Allowed(expectedExposedHeaders: new[] { expectedHeader })));

        var expectedSuccess = expectedHeader.Equals("X-Request-Id", StringComparison.OrdinalIgnoreCase) &&
            actualExposed is "*" or "X-Request-Id" && (!useCredentials || actualExposed == "X-Request-Id");
        Assert.Equal(expectedSuccess, result.IsSuccess);
        if (!expectedSuccess)
        {
            Assert.Contains(result.Issues, issue => issue.Kind == CorsFailureKind.ExposedHeadersMismatch);
        }
    }

    [Theory]
    [InlineData("DELETE, bad method", "methods")]
    [InlineData("DE LETE", "methods")]
    [InlineData("DELETE, DÉLETE", "methods")]
    [InlineData("DELETE,\u001f", "methods")]
    [InlineData("X-Trace, bad header", "headers")]
    [InlineData("X:Trace", "headers")]
    [InlineData("X-Request-Id, bad header", "exposed")]
    [InlineData("X:Request-Id", "exposed")]
    public async Task Malformed_cors_lists_fail_closed_even_when_the_expected_token_is_present(string value, string field)
    {
        var handler = new RecordingHandler(request => request.Method == HttpMethod.Options
            ? ResponseFactory.Cors(
                methods: field == "methods" ? value : "DELETE",
                headers: field == "headers" ? value : "X-Trace")
            : ResponseFactory.Cors(exposed: field == "exposed" ? value : "X-Request-Id", vary: null));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://service.test") };
        var expectedHeaders = field == "headers" ? new[] { "X-Trace" } : Array.Empty<string>();
        var result = await new CorsVerifier(client).VerifyAsync(new CorsContract(
            new CorsScenario("/orders", "https://app.example", HttpMethod.Delete, expectedHeaders),
            CorsExpectation.Allowed(expectedExposedHeaders: field == "exposed" ? new[] { "X-Request-Id" } : null)));

        Assert.False(result.IsSuccess, result.Summary);
    }

    [Fact]
    public async Task Valid_cors_lists_apply_field_specific_case_and_http_ows_rules()
    {
        var handler = new RecordingHandler(request => request.Method == HttpMethod.Options
            ? ResponseFactory.Cors(methods: " \tDELETE\t ", headers: " \tx-trace\t ", maxAge: " \t600\t ")
            : ResponseFactory.Cors(exposed: " \tx-request-id\t ", vary: null));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://service.test") };
        var result = await new CorsVerifier(client).VerifyAsync(new CorsContract(
            new CorsScenario("/orders", "https://app.example", HttpMethod.Delete, new[] { "X-Trace" }),
            CorsExpectation.Allowed(expectedExposedHeaders: new[] { "X-Request-Id" }, expectedMaxAge: TimeSpan.FromMinutes(10))));

        Assert.True(result.IsSuccess, result.Summary);
    }

    [Theory]
    [InlineData("+600")]
    [InlineData("600.0")]
    [InlineData("-1")]
    [InlineData("600x")]
    [InlineData("６００")]
    [InlineData("999999999999999999999999999999")]
    public async Task Max_age_requires_one_ascii_digit_only_value(string value)
    {
        var handler = new RecordingHandler(_ => ResponseFactory.Cors(maxAge: value));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://service.test") };
        var result = await new CorsVerifier(client).VerifyAsync(new CorsContract(
            new CorsScenario("/orders", "https://app.example", HttpMethod.Delete),
            CorsExpectation.Allowed(expectedMaxAge: TimeSpan.FromMinutes(10))));

        Assert.False(result.IsSuccess, result.Summary);
        Assert.Contains(result.Issues, issue => issue.Kind == CorsFailureKind.MaxAgeMismatch);
    }

    [Fact]
    public async Task Duplicate_max_age_values_are_rejected()
    {
        var handler = new RecordingHandler(_ =>
        {
            var response = ResponseFactory.Cors(maxAge: "600");
            response.Headers.TryAddWithoutValidation("Access-Control-Max-Age", "600");
            return response;
        });
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://service.test") };
        var result = await new CorsVerifier(client).VerifyAsync(new CorsContract(
            new CorsScenario("/orders", "https://app.example", HttpMethod.Delete),
            CorsExpectation.Allowed(expectedMaxAge: TimeSpan.FromMinutes(10))));

        Assert.False(result.IsSuccess, result.Summary);
        Assert.Contains(result.Issues, issue => issue.Kind == CorsFailureKind.MaxAgeMismatch);
    }

    [Theory]
    [InlineData("methods")]
    [InlineData("headers")]
    [InlineData("exposed")]
    public async Task Wildcard_list_members_follow_their_field_specific_grammar(string field)
    {
        var handler = new RecordingHandler(request => request.Method == HttpMethod.Options
            ? ResponseFactory.Cors(
                methods: field == "methods" ? "*, DELETE" : "DELETE",
                headers: field == "headers" ? "*, X-Trace" : "X-Trace")
            : ResponseFactory.Cors(exposed: field == "exposed" ? "*, X-Request-Id" : null, vary: null));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://service.test") };
        var result = await new CorsVerifier(client).VerifyAsync(new CorsContract(
            new CorsScenario("/orders", "https://app.example", HttpMethod.Delete, field == "headers" ? new[] { "X-Trace" } : null),
            CorsExpectation.Allowed(expectedExposedHeaders: field == "exposed" ? new[] { "X-Request-Id" } : null)));

        Assert.True(result.IsSuccess, result.Summary);
    }

    [Fact]
    public async Task Empty_allow_methods_is_valid_metadata_when_a_preflighted_simple_method_needs_no_method_match()
    {
        var handler = new RecordingHandler(request => request.Method == HttpMethod.Options
            ? ResponseFactory.Cors(methods: string.Empty, headers: "X-Trace")
            : ResponseFactory.Cors(vary: null));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://service.test") };

        var result = await new CorsVerifier(client).VerifyAsync(new CorsContract(
            new CorsScenario("/orders", "https://app.example", HttpMethod.Get, new[] { "X-Trace" }),
            CorsExpectation.Allowed()));

        Assert.True(result.IsSuccess, result.Summary);
        Assert.True(result.PreflightSent);
        Assert.True(result.ActualRequestSent);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Empty_allow_headers_is_valid_metadata_when_no_requested_header_needs_matching()
    {
        var handler = new RecordingHandler(request => request.Method == HttpMethod.Options
            ? ResponseFactory.Cors(methods: "DELETE", headers: string.Empty)
            : ResponseFactory.Cors(vary: null));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://service.test") };

        var result = await new CorsVerifier(client).VerifyAsync(new CorsContract(
            new CorsScenario("/orders", "https://app.example", HttpMethod.Delete),
            CorsExpectation.Allowed()));

        Assert.True(result.IsSuccess, result.Summary);
        Assert.True(result.PreflightSent);
        Assert.True(result.ActualRequestSent);
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
    public async Task Wildcards_do_not_cover_authorization()
    {
        var authorizationHandler = new RecordingHandler(_ => ResponseFactory.Cors(headers: "*"));
        using var authorizationClient = new HttpClient(authorizationHandler) { BaseAddress = new Uri("https://service.test") };
        var authorizationResult = await new CorsVerifier(authorizationClient).VerifyAsync(new CorsContract(
            new CorsScenario("/orders", "https://app.example", HttpMethod.Delete, new[] { "Authorization" }),
            CorsExpectation.Allowed()));

        Assert.False(authorizationResult.IsSuccess);
        Assert.Contains(authorizationResult.Issues, issue => issue.Kind == CorsFailureKind.RequestedHeaderRejected);
    }

    [Fact]
    public async Task Header_wildcard_can_be_combined_with_an_explicit_authorization_member()
    {
        var handler = new RecordingHandler(request => request.Method == HttpMethod.Options
            ? ResponseFactory.Cors(headers: "*, Authorization")
            : ResponseFactory.Cors(vary: null));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://service.test") };

        var result = await new CorsVerifier(client, handler.CreateSibling).VerifyAsync(new CorsContract(
            new CorsScenario("/orders", "https://app.example", HttpMethod.Delete, new[] { "Authorization" }),
            CorsExpectation.Allowed()));

        Assert.True(result.IsSuccess, result.Summary);
    }

    [Fact]
    public async Task Empty_cors_list_members_are_ignored_when_a_valid_member_remains()
    {
        var handler = new RecordingHandler(request => request.Method == HttpMethod.Options
            ? ResponseFactory.Cors(methods: "DELETE,,", headers: "X-Trace,,Authorization")
            : ResponseFactory.Cors(exposed: "X-Request-Id,,", vary: null));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://service.test") };

        var result = await new CorsVerifier(client, handler.CreateSibling).VerifyAsync(new CorsContract(
            new CorsScenario("/orders", "https://app.example", HttpMethod.Delete, new[] { "X-Trace", "Authorization" }),
            CorsExpectation.Allowed(expectedExposedHeaders: new[] { "X-Request-Id" })));

        Assert.True(result.IsSuccess, result.Summary);
    }

    [Fact]
    public async Task Malformed_allow_headers_are_rejected_even_without_requested_headers()
    {
        var handler = new RecordingHandler(request => request.Method == HttpMethod.Options
            ? ResponseFactory.Cors(methods: "DELETE", headers: "bad header")
            : ResponseFactory.Cors(vary: null));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://service.test") };

        var result = await new CorsVerifier(client, handler.CreateSibling).VerifyAsync(new CorsContract(
            new CorsScenario("/orders", "https://app.example", HttpMethod.Delete),
            CorsExpectation.Allowed()));

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Kind == CorsFailureKind.RequestedHeaderRejected);
        Assert.False(result.ActualRequestSent);
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

    [Fact]
    public async Task Denied_preflight_grant_is_followed_by_actual_response_evaluation()
    {
        var handler = new RecordingHandler(request => request.Method == HttpMethod.Options
            ? ResponseFactory.Cors(origin: "https://untrusted.example", methods: "DELETE")
            : ResponseFactory.Cors(origin: "https://untrusted.example", vary: null));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://service.test") };

        var result = await new CorsVerifier(client, handler.CreateSibling).VerifyAsync(new CorsContract(
            new CorsScenario("/orders", "https://untrusted.example", HttpMethod.Delete),
            CorsExpectation.Denied()));

        Assert.False(result.IsSuccess);
        Assert.True(result.PreflightSent);
        Assert.True(result.ActualRequestSent);
        Assert.Contains(result.Issues, issue => issue.Kind == CorsFailureKind.UnexpectedCorsPermission);
    }
}
