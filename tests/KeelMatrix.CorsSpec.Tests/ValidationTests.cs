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

    [Theory]
    [InlineData("https://service.test/orders")]
    [InlineData("http:orders")]
    [InlineData("custom+scheme:orders")]
    [InlineData("//other-host/orders")]
    [InlineData("\\\\evil\\share")]
    [InlineData("/\\evil\\share")]
    [InlineData("\\/evil/share")]
    [InlineData("\\\\?\\C:\\orders")]
    [InlineData("\\\\.\\pipe\\orders")]
    [InlineData("\\orders")]
    [InlineData("C:orders")]
    [InlineData("C:\\orders")]
    public void Scenario_rejects_absolute_authority_and_windows_separator_paths(string path)
    {
        var handler = new RecordingHandler(_ => throw new InvalidOperationException("request should not execute"));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://service.test") };

        Assert.Throws<ArgumentException>(() => new CorsScenario(path, "https://app.example", HttpMethod.Get));
        Assert.Empty(handler.Requests);
    }

    public static IEnumerable<object[]> RootRelativeWindowsDrivePathCases()
    {
        var suffixes = new[]
        {
            "",
            "/",
            "?status=open",
            "#fragment",
            "orders",
            "orders/nested",
            "orders?status=open",
            "orders#fragment",
        };

        foreach (var drive in Enumerable.Range('A', 'Z' - 'A' + 1).Select(static value => (char)value))
        {
            foreach (var suffix in suffixes)
            {
                yield return new object[] { $"/{drive}:{suffix}" };
                yield return new object[] { $"/{char.ToLowerInvariant(drive)}:{suffix}" };
            }
        }
    }

    [Theory]
    [MemberData(nameof(RootRelativeWindowsDrivePathCases))]
    public void Scenario_rejects_root_relative_windows_drive_paths_before_uri_construction_and_io(string path)
    {
        var handler = new RecordingHandler(_ => throw new InvalidOperationException("request should not execute"));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://service.test") };

        Assert.Throws<ArgumentException>(() => new CorsScenario(path, "https://app.example", HttpMethod.Get));
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData("CONNECT")]
    [InlineData("connect")]
    [InlineData("TRACE")]
    [InlineData("trace")]
    [InlineData("TRACK")]
    [InlineData("track")]
    public void Browser_forbidden_methods_are_rejected_before_io(string method)
    {
        var handler = new RecordingHandler(_ => throw new InvalidOperationException("request should not execute"));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://service.test") };

        Assert.Throws<ArgumentException>(() => new CorsScenario("/orders", "https://app.example", new HttpMethod(method)));
        Assert.Empty(handler.Requests);
    }

    public static IEnumerable<object[]> BrowserMethodCasingCases()
    {
        foreach (var method in new[] { "DELETE", "GET", "HEAD", "OPTIONS", "POST", "PUT" })
        {
            foreach (var variant in CasingVariants(method))
            {
                yield return new object[] { variant, method, method is "GET" or "HEAD" or "POST" };
            }
        }
    }

    [Theory]
    [MemberData(nameof(BrowserMethodCasingCases))]
    public async Task Browser_method_casing_is_normalized_before_classification_and_request_construction(
        string input,
        string expected,
        bool isSimple)
    {
        var handler = new RecordingHandler(request => request.Method == HttpMethod.Options
            ? ResponseFactory.Cors(methods: expected)
            : ResponseFactory.Cors());
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://service.test") };
        var scenario = new CorsScenario("/orders", "https://app.example", new HttpMethod(input));

        var result = await new CorsVerifier(client).VerifyAsync(new CorsContract(scenario, CorsExpectation.Allowed()));

        Assert.True(result.IsSuccess, result.Summary);
        Assert.Equal(expected, scenario.Method.Method);
        Assert.Equal(!isSimple, result.PreflightSent);
        Assert.Equal(isSimple ? 1 : 2, handler.Requests.Count);
        if (!isSimple)
        {
            Assert.Equal(expected, handler.Requests[0].Headers.GetValues("Access-Control-Request-Method").Single());
        }

        Assert.Equal(expected, handler.Requests[^1].Method.Method);
    }

    private static IEnumerable<string> CasingVariants(string value, int index = 0)
    {
        if (index == value.Length)
        {
            yield return string.Empty;
            yield break;
        }

        foreach (var suffix in CasingVariants(value, index + 1))
        {
            yield return char.ToUpperInvariant(value[index]) + suffix;
            yield return char.ToLowerInvariant(value[index]) + suffix;
        }
    }

    [Fact]
    public async Task Custom_method_casing_remains_unchanged()
    {
        const string customMethod = "pAtCh";
        var handler = new RecordingHandler(request => request.Method == HttpMethod.Options
            ? ResponseFactory.Cors(methods: customMethod)
            : ResponseFactory.Cors());
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://service.test") };
        var scenario = new CorsScenario("/orders", "https://app.example", new HttpMethod(customMethod));

        var result = await new CorsVerifier(client).VerifyAsync(new CorsContract(scenario, CorsExpectation.Allowed()));

        Assert.True(result.IsSuccess, result.Summary);
        Assert.Equal(customMethod, scenario.Method.Method);
        Assert.Equal(customMethod, handler.Requests[0].Headers.GetValues("Access-Control-Request-Method").Single());
        Assert.Equal(customMethod, handler.Requests[1].Method.Method);
    }

    public static IEnumerable<object[]> RawControlPathCases()
    {
        foreach (var codePoint in Enumerable.Range(0, 32).Append(0x7f))
        {
            yield return new object[] { "/orders" + (char)codePoint };
        }
    }

    [Theory]
    [MemberData(nameof(RawControlPathCases))]
    public void Scenario_rejects_every_raw_c0_control_and_del_path_before_io(string path)
    {
        var handler = new RecordingHandler(_ => throw new InvalidOperationException("request should not execute"));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://service.test") };

        Assert.Throws<ArgumentException>(() => new CorsScenario(path, "https://app.example", HttpMethod.Get));
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData("/orders")]
    [InlineData("orders")]
    [InlineData("/orders?status=open")]
    [InlineData("/orders/%00")]
    [InlineData("/orders/%09")]
    [InlineData("/orders/%1F")]
    [InlineData("/orders/%7F")]
    public void Relative_paths_and_percent_encoded_control_data_remain_valid(string path)
    {
        var scenario = new CorsScenario(path, "https://app.example", HttpMethod.Get);
        using var request = CorsVerifier.CreateActualRequest(scenario);

        Assert.False(request.RequestUri!.IsAbsoluteUri);
    }

    public static IEnumerable<object[]> SchemeLikeDelimiterCases()
    {
        foreach (var path in new[]
        {
            "/redirect?next=https://external.example/orders",
            "redirect?next=https://external.example/orders",
            "/redirect?next=https://",
            "/redirect?next=://external.example/orders",
            "/route?next=/C:foo",
            "/route?next=/c:orders",
            "/route?next=/C:orders?x=1",
            "/route#next=/C:foo",
            "/redirect?next=https://external.example/orders&tail=1",
            "/redirect?next=https%3A%2F%2Fexternal.example/orders",
            "/route/%2FC%3Aorders",
            "/route?next=%2FC%3Aorders",
            "/redirect#next=https://external.example/orders",
            "redirect#next=https://external.example/orders",
        })
        {
            yield return new object[] { path };
        }
    }

    [Theory]
    [MemberData(nameof(SchemeLikeDelimiterCases))]
    public async Task Relative_targets_accept_and_send_scheme_like_query_or_fragment_data(string path)
    {
        var handler = new RecordingHandler(_ => ResponseFactory.Cors());
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://service.test") };

        var result = await new CorsVerifier(client).VerifyAsync(new CorsContract(
            new CorsScenario(path, "https://app.example", HttpMethod.Get),
            CorsExpectation.Allowed()));

        Assert.True(result.IsSuccess, result.Summary);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(new Uri(new Uri("https://service.test"), path).PathAndQuery, request.RequestUri!.PathAndQuery);
        Assert.Equal(new Uri(new Uri("https://service.test"), path).Fragment, request.RequestUri.Fragment);
    }

    [Theory]
    [InlineData("https://app.example/")]
    [InlineData("https://app.example:443")]
    [InlineData("http://app.example:80")]
    [InlineData("HTTPS://APP.EXAMPLE")]
    [InlineData("https://[2001:DB8::1]")]
    [InlineData("https://[2001:db8::1]:443")]
    [InlineData("https://user:pass@app.example")]
    [InlineData("https://app.example/orders")]
    [InlineData("https://app.example?query=1")]
    [InlineData("https://app.example#fragment")]
    [InlineData("ftp://app.example")]
    public void Scenario_rejects_non_canonical_or_non_http_origins(string origin)
    {
        Assert.Throws<ArgumentException>(() => new CorsScenario("/orders", origin, HttpMethod.Get));
    }

    [Fact]
    public void Scenario_accepts_canonical_and_null_origins()
    {
        Assert.Equal("https://app.example", new CorsScenario("/orders", "https://app.example", HttpMethod.Get).Origin);
        Assert.Equal("null", new CorsScenario("/orders", "null", HttpMethod.Get).Origin);
    }

    [Theory]
    [InlineData("Content-Type")]
    [InlineData("Range")]
    public void Value_sensitive_header_names_without_values_force_preflight(string header)
    {
        var scenario = new CorsScenario("/orders", "https://app.example", HttpMethod.Get, new[] { header });

        Assert.True(scenario.RequiresPreflight);
    }

    [Theory]
    [InlineData("X-Trace")]
    public void Value_sensitive_and_custom_headers_force_a_preflight(string header)
    {
        var scenario = new CorsScenario("/orders", "https://app.example", HttpMethod.Get, new[] { header });

        Assert.True(scenario.RequiresPreflight);
    }

    [Theory]
    [InlineData("Cookie")]
    [InlineData("Origin")]
    [InlineData("Host")]
    [InlineData("Sec-Fetch-Site")]
    [InlineData("Access-Control-Request-Method")]
    [InlineData("Proxy-Authorization")]
    [InlineData("X-HTTP-Method")]
    [InlineData("x-http-method-override")]
    [InlineData("X-Method-Override")]
    [InlineData("Set-Cookie2")]
    public void Browser_managed_and_cors_protocol_headers_are_rejected(string header)
    {
        Assert.Throws<ArgumentException>(() => new CorsScenario("/orders", "https://app.example", HttpMethod.Get, new[] { header }));
    }

    [Theory]
    [InlineData(" X-HTTP-Method")]
    [InlineData("X-HTTP-Method ")]
    [InlineData("X-HTTP-Method\t")]
    public void Conditional_override_header_whitespace_is_rejected_before_io(string header)
    {
        var handler = new RecordingHandler(_ => throw new InvalidOperationException("request should not execute"));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://service.test") };

        Assert.Throws<ArgumentException>(() => new CorsScenario("/orders", "https://app.example", HttpMethod.Get, new[] { header }));
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData("Set-Cookie")]
    [InlineData("set-cookie")]
    [InlineData("SET-COOKIE2")]
    [InlineData("Set-Cookie2")]
    public void Forbidden_response_header_expectations_fail_before_io(string header)
    {
        var handler = new RecordingHandler(_ => throw new InvalidOperationException("request should not execute"));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://service.test") };

        var exception = Assert.Throws<ArgumentException>(() =>
            CorsExpectation.Allowed(expectedExposedHeaders: new[] { header, header }));

        Assert.Contains("browsers forbid exposing", exception.Message);
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData("X-Trace")]
    [InlineData("x_trace")]
    [InlineData("!#$%&'*+-.^_`|~")]
    [InlineData("X-Éclair")]
    [InlineData("X-数字")]
    [InlineData("X Trace")]
    [InlineData("X,Trace")]
    [InlineData("X:Trace")]
    [InlineData("X\tTrace")]
    [InlineData("")]
    public void Header_names_keep_the_ascii_token_boundary(string header)
    {
        if (header is "X-Trace" or "x_trace" or "!#$%&'*+-.^_`|~")
        {
            var scenario = new CorsScenario("/orders", "https://app.example", HttpMethod.Get, new[] { header });
            Assert.Single(scenario.RequestedHeaders);
        }
        else
        {
            Assert.Throws<ArgumentException>(() => new CorsScenario("/orders", "https://app.example", HttpMethod.Get, new[] { header }));
        }
    }

    [Fact]
    public void Matrix_is_bounded()
    {
        var scenario = new CorsScenario("/orders", "https://app.example", HttpMethod.Get);
        var contracts = Enumerable.Repeat(new CorsContract(scenario, CorsExpectation.Denied()), 257);

        Assert.Throws<ArgumentException>(() => new CorsMatrix(contracts));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(256)]
    public void Matrix_accepts_contracts_at_the_upper_boundary(int count)
    {
        var scenario = new CorsScenario("/orders", "https://app.example", HttpMethod.Get);
        var matrix = new CorsMatrix(Enumerable.Repeat(new CorsContract(scenario, CorsExpectation.Denied()), count));

        Assert.Equal(count, matrix.Contracts.Count);
    }

    [Fact]
    public void Matrix_rejects_empty_and_null_contracts_during_enumeration()
    {
        Assert.Throws<ArgumentException>(() => new CorsMatrix(Array.Empty<CorsContract>()));
        var scenario = new CorsScenario("/orders", "https://app.example", HttpMethod.Get);
        var contract = new CorsContract(scenario, CorsExpectation.Denied());

        Assert.Throws<ArgumentException>(() => new CorsMatrix(new CorsContract?[] { contract, null }!));
    }

    [Fact]
    public void Matrix_stops_enumerating_at_the_bound_for_infinite_inputs()
    {
        var scenario = new CorsScenario("/orders", "https://app.example", HttpMethod.Get);
        var contract = new CorsContract(scenario, CorsExpectation.Denied());
        var enumerated = 0;

        IEnumerable<CorsContract> InfiniteContracts()
        {
            while (true)
            {
                enumerated++;
                yield return contract;
            }
        }

        Assert.Throws<ArgumentException>(() => new CorsMatrix(InfiniteContracts()));
        Assert.Equal(257, enumerated);
    }

    [Fact]
    public void Matrix_propagates_enumerator_failures_without_materializing_unbounded_input()
    {
        IEnumerable<CorsContract> ThrowingContracts()
        {
            yield return new CorsContract(new CorsScenario("/orders", "https://app.example", HttpMethod.Get), CorsExpectation.Denied());
            throw new InvalidOperationException("enumerator failure");
        }

        Assert.Throws<InvalidOperationException>(() => new CorsMatrix(ThrowingContracts()));
    }

    [Fact]
    public async Task Public_collections_are_read_only_snapshots()
    {
        var scenario = new CorsScenario("/orders", "https://app.example", HttpMethod.Get, new[] { "X-Trace" });
        var expectation = CorsExpectation.Allowed(expectedExposedHeaders: new[] { "X-Request-Id" });
        var matrix = new CorsMatrix(new[] { new CorsContract(scenario, expectation) });
        var handler = new RecordingHandler(_ => ResponseFactory.Cors(origin: "https://other.example"));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://service.test") };
        var result = await new CorsVerifier(client).VerifyAsync(new CorsContract(scenario, expectation));

        AssertReadOnly(scenario.RequestedHeaders);
        AssertReadOnly(expectation.ExpectedExposedHeaders);
        AssertReadOnly(matrix.Contracts);
        AssertReadOnly(result.Issues);
        var summary = result.Summary;
        Assert.Equal(summary, result.Summary);
    }

    private static void AssertReadOnly<T>(IReadOnlyList<T> values)
    {
        Assert.False(values is T[]);
        var list = Assert.IsAssignableFrom<IList<T>>(values);
        Assert.True(list.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => list.Add(default!));
    }

    [Fact]
    public async Task Matrix_verification_returns_one_result_per_contract()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://service.test") };
        var scenario = new CorsScenario("/orders", "https://app.example", HttpMethod.Get);
        var matrix = new CorsMatrix(new[]
        {
            new CorsContract(scenario, CorsExpectation.Denied()),
            new CorsContract(new CorsScenario("/orders", "https://other.example", HttpMethod.Get), CorsExpectation.Denied())
        });

        var results = await new CorsVerifier(client).VerifyMatrixAsync(matrix);

        Assert.Equal(2, results.Count);
        Assert.All(results, result => Assert.True(result.IsSuccess, result.Summary));
    }
}
