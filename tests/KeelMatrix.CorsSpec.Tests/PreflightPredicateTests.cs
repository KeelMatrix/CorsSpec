using System.Net;
using System.Net.Http;
using KeelMatrix.CorsSpec;

namespace KeelMatrix.CorsSpec.Tests;

public sealed class PreflightPredicateTests
{
    public static IEnumerable<object[]> DefaultHeaderBoundaryCases()
    {
        foreach (var header in new[] { "Accept", "Accept-Language", "Content-Language" })
        {
            yield return new object[] { header, SafeValue(header), false };
            yield return new object[] { header, UnsafeValue(header), true };
            yield return new object[] { header, new string('a', 128), false };
            yield return new object[] { header, new string('a', 129), true };
            if (header != "Content-Language")
            {
                yield return new object[] { header, "a\tvalue", true };
            }
        }
    }

    [Theory]
    [InlineData("Accept")]
    [InlineData("Accept-Language")]
    [InlineData("Content-Language")]
    public void Control_character_values_are_rejected_before_request_classification(string header)
    {
        Assert.Throws<ArgumentException>(() => new CorsRequestHeader(header, "a\tvalue"));
    }

    [Theory]
    [MemberData(nameof(DefaultHeaderBoundaryCases))]
    public async Task Effective_default_header_classification_controls_preflight_and_max_age_validation(
        string header,
        string value,
        bool requiresPreflight)
    {
        var handler = new RecordingHandler(request => request.Method == HttpMethod.Options
            ? ResponseFactory.Cors(headers: header, maxAge: "60")
            : ResponseFactory.Cors());
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://service.test") };
        CorsScenario scenario;
        if (header == "Content-Language")
        {
            scenario = new CorsScenario(
                "/orders",
                "https://app.example",
                HttpMethod.Get,
                requestHeaders: new[] { new CorsRequestHeader(header, value) });
        }
        else
        {
            client.DefaultRequestHeaders.TryAddWithoutValidation(header, value);
            scenario = new CorsScenario("/orders", "https://app.example", HttpMethod.Get, new[] { header });
        }

        var result = await new CorsVerifier(client, handler.CreateSibling).VerifyAsync(new CorsContract(
            scenario,
            CorsExpectation.Allowed(expectedMaxAge: TimeSpan.FromSeconds(60))));

        Assert.Equal(requiresPreflight, result.PreflightSent);
        Assert.Equal(requiresPreflight, result.ActualRequestSent);
        Assert.Equal(requiresPreflight ? 2 : 0, handler.Requests.Count);
        if (requiresPreflight)
        {
            Assert.True(result.IsSuccess, result.Summary);
            Assert.Equal(header.ToLowerInvariant(), handler.Requests[0].Headers.GetValues("Access-Control-Request-Headers").Single());
        }
        else
        {
            Assert.False(result.IsSuccess);
            Assert.Contains(result.Issues, issue => issue.Kind == CorsFailureKind.MalformedScenario);
        }
    }

    public static IEnumerable<object[]> ScenarioAndDefaultHeaderCases()
    {
        foreach (var header in new[] { "Accept", "Accept-Language" })
        {
            yield return new object[] { header, SafeValue(header), UnsafeValue(header) };
            yield return new object[] { header, UnsafeValue(header), SafeValue(header) };
        }
    }

    [Theory]
    [MemberData(nameof(ScenarioAndDefaultHeaderCases))]
    public async Task Explicit_scenario_value_overrides_same_named_default_for_classification_and_actual_request(
        string header,
        string defaultValue,
        string scenarioValue)
    {
        var handler = new RecordingHandler(request => request.Method == HttpMethod.Options
            ? ResponseFactory.Cors(headers: header, maxAge: "60")
            : ResponseFactory.Cors(vary: null));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://service.test") };
        client.DefaultRequestHeaders.TryAddWithoutValidation(header, defaultValue);

        var requiresPreflight = header == "Accept" ? scenarioValue == "text/html?" : scenarioValue == "en_US";
        var result = await new CorsVerifier(client, handler.CreateSibling).VerifyAsync(new CorsContract(
            new CorsScenario(
                "/orders",
                "https://app.example",
                HttpMethod.Get,
                new[] { header },
                requestHeaders: new[] { new CorsRequestHeader(header, scenarioValue) }),
            requiresPreflight
                ? CorsExpectation.Allowed(expectedMaxAge: TimeSpan.FromSeconds(60))
                : CorsExpectation.Allowed()));

        Assert.True(result.IsSuccess, result.Summary);
        Assert.Equal(requiresPreflight, result.PreflightSent);
        Assert.True(result.ActualRequestSent);
        Assert.Equal(requiresPreflight ? 2 : 1, handler.Requests.Count);
        var actual = Assert.Single(handler.Requests, request => request.Method == HttpMethod.Get);
        Assert.Equal(scenarioValue, actual.Headers.GetValues(header).Single());
    }

    [Fact]
    public async Task Explicit_safe_override_keeps_a_denied_contract_on_the_actual_response_path()
    {
        const string header = "Accept";
        var handler = new RecordingHandler(request => request.Method == HttpMethod.Options
            ? new HttpResponseMessage(HttpStatusCode.MethodNotAllowed)
            : new HttpResponseMessage(HttpStatusCode.OK));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://service.test") };
        client.DefaultRequestHeaders.TryAddWithoutValidation(header, "text/html?");

        var result = await new CorsVerifier(client).VerifyAsync(new CorsContract(
            new CorsScenario(
                "/orders",
                "https://app.example",
                HttpMethod.Get,
                new[] { header },
                requestHeaders: new[] { new CorsRequestHeader(header, "text/html") }),
            CorsExpectation.Denied()));

        Assert.True(result.IsSuccess, result.Summary);
        Assert.False(result.PreflightSent);
        Assert.True(result.ActualRequestSent);
        Assert.Single(handler.Requests);
        Assert.Equal("text/html", handler.Requests[0].Headers.GetValues(header).Single());
    }

    [Fact]
    public async Task Non_safelisted_default_header_uses_the_same_effective_predicate()
    {
        const string header = "X-Trace";
        var handler = new RecordingHandler(request => request.Method == HttpMethod.Options
            ? ResponseFactory.Cors(headers: header, maxAge: "60")
            : ResponseFactory.Cors());
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://service.test") };
        client.DefaultRequestHeaders.TryAddWithoutValidation(header, "caller-default");

        var result = await new CorsVerifier(client, handler.CreateSibling).VerifyAsync(new CorsContract(
            new CorsScenario("/orders", "https://app.example", HttpMethod.Get, new[] { header }),
            CorsExpectation.Allowed(expectedMaxAge: TimeSpan.FromSeconds(60))));

        Assert.True(result.IsSuccess, result.Summary);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task One_argument_constructor_retains_declared_default_header_isolation_guard()
    {
        const string header = "Accept";
        var handler = new RecordingHandler(_ => ResponseFactory.Cors());
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://service.test") };
        client.DefaultRequestHeaders.TryAddWithoutValidation(header, UnsafeValue(header));

        await Assert.ThrowsAsync<InvalidOperationException>(() => new CorsVerifier(client).VerifyAsync(new CorsContract(
            new CorsScenario("/orders", "https://app.example", HttpMethod.Get, new[] { header }),
            CorsExpectation.Allowed(expectedMaxAge: TimeSpan.FromSeconds(60)))));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Effective_preflight_can_be_sent_but_an_incorrect_max_age_still_fails()
    {
        const string header = "Accept";
        var handler = new RecordingHandler(request => request.Method == HttpMethod.Options
            ? ResponseFactory.Cors(headers: header, maxAge: "60")
            : ResponseFactory.Cors());
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://service.test") };
        client.DefaultRequestHeaders.TryAddWithoutValidation(header, UnsafeValue(header));

        var result = await new CorsVerifier(client, handler.CreateSibling).VerifyAsync(new CorsContract(
            new CorsScenario("/orders", "https://app.example", HttpMethod.Get, new[] { header }),
            CorsExpectation.Allowed(expectedMaxAge: TimeSpan.FromSeconds(61))));

        Assert.False(result.IsSuccess);
        Assert.True(result.PreflightSent);
        Assert.True(result.ActualRequestSent);
        Assert.Contains(result.Issues, issue => issue.Kind == CorsFailureKind.MaxAgeMismatch);
    }

    [Theory]
    [InlineData(1024, false)]
    [InlineData(1025, true)]
    public async Task Aggregate_safelisted_value_size_controls_preflight_at_the_1024_byte_boundary(int totalBytes, bool requiresPreflight)
    {
        var handler = new RecordingHandler(request => request.Method == HttpMethod.Options
            ? ResponseFactory.Cors(methods: null, headers: "Accept", maxAge: "60")
            : ResponseFactory.Cors(vary: null));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://service.test") };
        AddDefaultValues(client, "Accept", totalBytes);

        var result = await new CorsVerifier(client, handler.CreateSibling).VerifyAsync(new CorsContract(
            new CorsScenario("/orders", "https://app.example", HttpMethod.Get, new[] { "Accept" }),
            requiresPreflight
                ? CorsExpectation.Allowed(expectedMaxAge: TimeSpan.FromSeconds(60))
                : CorsExpectation.Allowed()));

        Assert.True(result.IsSuccess, result.Summary);
        Assert.Equal(requiresPreflight, result.PreflightSent);
        Assert.True(result.ActualRequestSent);
        Assert.Equal(requiresPreflight ? 2 : 1, handler.Requests.Count);
        if (requiresPreflight)
        {
            Assert.Equal(HttpMethod.Options, handler.Requests[0].Method);
            Assert.Equal(HttpMethod.Get, handler.Requests[1].Method);
        }
    }

    [Theory]
    [InlineData(64, false)]
    [InlineData(65, true)]
    public async Task Per_value_limit_uses_the_supported_utf8_byte_representation(int repeatedCharacterCount, bool requiresPreflight)
    {
        var value = new string('é', repeatedCharacterCount);
        var handler = new RecordingHandler(request => request.Method == HttpMethod.Options
            ? ResponseFactory.Cors(methods: null, headers: "Accept", maxAge: "60")
            : ResponseFactory.Cors(vary: null));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://service.test") };

        var result = await new CorsVerifier(client, handler.CreateSibling).VerifyAsync(new CorsContract(
            new CorsScenario(
                "/orders",
                "https://app.example",
                HttpMethod.Get,
                new[] { "Accept" },
                requestHeaders: new[] { new CorsRequestHeader("Accept", value) }),
            requiresPreflight
                ? CorsExpectation.Allowed(expectedMaxAge: TimeSpan.FromSeconds(60))
                : CorsExpectation.Allowed()));

        Assert.True(result.IsSuccess, result.Summary);
        Assert.Equal(requiresPreflight, result.PreflightSent);
        Assert.Equal(requiresPreflight ? 2 : 1, handler.Requests.Count);
    }

    [Fact]
    public async Task Aggregate_overflow_keeps_already_unsafe_names_in_the_canonical_preflight_list()
    {
        var handler = new RecordingHandler(request => request.Method == HttpMethod.Options
            ? ResponseFactory.Cors(methods: null, headers: "Accept, Accept-Language, X-Trace", maxAge: "60")
            : ResponseFactory.Cors(vary: null));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://service.test") };
        AddDefaultValues(client, "Accept", 512);
        AddDefaultValues(client, "Accept-Language", 513);

        var result = await new CorsVerifier(client, handler.CreateSibling).VerifyAsync(new CorsContract(
            new CorsScenario("/orders", "https://app.example", HttpMethod.Get, new[] { "X-Trace", "Accept", "Accept-Language" }),
            CorsExpectation.Allowed(expectedMaxAge: TimeSpan.FromSeconds(60))));

        Assert.True(result.IsSuccess, result.Summary);
        Assert.Equal(new[] { "accept, accept-language, x-trace" }, handler.Requests[0].Headers.GetValues("Access-Control-Request-Headers"));
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Aggregate_safelisted_value_size_includes_values_across_header_names()
    {
        var handler = new RecordingHandler(request => request.Method == HttpMethod.Options
            ? ResponseFactory.Cors(methods: null, headers: "Accept, Accept-Language", maxAge: "60")
            : ResponseFactory.Cors(vary: null));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://service.test") };
        AddDefaultValues(client, "Accept", 512);
        AddDefaultValues(client, "Accept-Language", 513);

        var result = await new CorsVerifier(client, handler.CreateSibling).VerifyAsync(new CorsContract(
            new CorsScenario("/orders", "https://app.example", HttpMethod.Get, new[] { "Accept", "Accept-Language" }),
            CorsExpectation.Allowed(expectedMaxAge: TimeSpan.FromSeconds(60))));

        Assert.True(result.IsSuccess, result.Summary);
        Assert.True(result.PreflightSent);
        Assert.True(result.ActualRequestSent);
        Assert.Equal(new[] { "accept, accept-language" }, handler.Requests[0].Headers.GetValues("Access-Control-Request-Headers"));
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Aggregate_safelisted_value_size_ignores_overridden_default_values()
    {
        var handler = new RecordingHandler(request => request.Method == HttpMethod.Options
            ? ResponseFactory.Cors(methods: null, headers: "Accept", maxAge: "60")
            : ResponseFactory.Cors(vary: null));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://service.test") };
        AddDefaultValues(client, "Accept", 1024);

        var result = await new CorsVerifier(client, handler.CreateSibling).VerifyAsync(new CorsContract(
            new CorsScenario(
                "/orders",
                "https://app.example",
                HttpMethod.Get,
                new[] { "Accept" },
                requestHeaders: new[] { new CorsRequestHeader("Accept", "a") }),
            CorsExpectation.Allowed()));

        Assert.True(result.IsSuccess, result.Summary);
        Assert.False(result.PreflightSent);
        Assert.True(result.ActualRequestSent);
        Assert.Single(handler.Requests);
        Assert.Equal("a", handler.Requests[0].Headers.GetValues("Accept").Single());
    }

    [Fact]
    public async Task Aggregate_over_limit_is_detected_before_one_argument_default_header_guard()
    {
        var handler = new RecordingHandler(_ => throw new InvalidOperationException("request should not execute"));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://service.test") };
        AddDefaultValues(client, "Accept", 1025);

        await Assert.ThrowsAsync<InvalidOperationException>(() => new CorsVerifier(client).VerifyAsync(new CorsContract(
            new CorsScenario("/orders", "https://app.example", HttpMethod.Get, new[] { "Accept" }),
            CorsExpectation.Allowed(expectedMaxAge: TimeSpan.FromSeconds(60)))));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Aggregate_at_limit_remains_simple_with_one_argument_constructor()
    {
        var handler = new RecordingHandler(_ => ResponseFactory.Cors(vary: null));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://service.test") };
        AddDefaultValues(client, "Accept", 1024);

        var result = await new CorsVerifier(client).VerifyAsync(new CorsContract(
            new CorsScenario("/orders", "https://app.example", HttpMethod.Get, new[] { "Accept" }),
            CorsExpectation.Allowed()));

        Assert.True(result.IsSuccess, result.Summary);
        Assert.False(result.PreflightSent);
        Assert.True(result.ActualRequestSent);
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData("bytes=0-0", false)]
    [InlineData("bytes=10-1", true)]
    public async Task Effective_range_default_values_use_the_same_preflight_classifier(string value, bool requiresPreflight)
    {
        var handler = new RecordingHandler(request => request.Method == HttpMethod.Options
            ? ResponseFactory.Cors(methods: null, headers: "Range")
            : ResponseFactory.Cors(vary: null));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://service.test") };
        client.DefaultRequestHeaders.TryAddWithoutValidation("Range", value);

        var result = await new CorsVerifier(client, handler.CreateSibling).VerifyAsync(new CorsContract(
            new CorsScenario("/orders", "https://app.example", HttpMethod.Get, new[] { "Range" }),
            CorsExpectation.Allowed()));

        Assert.True(result.IsSuccess, result.Summary);
        Assert.Equal(requiresPreflight, result.PreflightSent);
        Assert.Equal(requiresPreflight ? 2 : 1, handler.Requests.Count);
        Assert.True(result.ActualRequestSent);
    }

    private static void AddDefaultValues(HttpClient client, string name, int totalBytes)
    {
        while (totalBytes > 0)
        {
            var valueLength = Math.Min(128, totalBytes);
            var value = new string(name.Equals("Accept-Language", StringComparison.OrdinalIgnoreCase) ? 'a' : 'x', valueLength);
            client.DefaultRequestHeaders.TryAddWithoutValidation(name, value);
            totalBytes -= valueLength;
        }
    }

    private static string SafeValue(string header) => header == "Accept" ? "text/html" : "en-US";

    private static string UnsafeValue(string header) => header == "Accept" ? "text/html?" : "en_US";
}
