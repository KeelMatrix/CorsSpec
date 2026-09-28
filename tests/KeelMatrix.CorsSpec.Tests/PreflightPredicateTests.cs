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
    public async Task Effective_classification_includes_both_scenario_and_default_values(
        string header,
        string defaultValue,
        string scenarioValue)
    {
        var handler = new RecordingHandler(request => request.Method == HttpMethod.Options
            ? ResponseFactory.Cors(headers: header, maxAge: "60")
            : ResponseFactory.Cors());
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://service.test") };
        client.DefaultRequestHeaders.TryAddWithoutValidation(header, defaultValue);

        var result = await new CorsVerifier(client, handler.CreateSibling).VerifyAsync(new CorsContract(
            new CorsScenario(
                "/orders",
                "https://app.example",
                HttpMethod.Get,
                new[] { header },
                requestHeaders: new[] { new CorsRequestHeader(header, scenarioValue) }),
            CorsExpectation.Allowed(expectedMaxAge: TimeSpan.FromSeconds(60))));

        Assert.True(result.IsSuccess, result.Summary);
        Assert.True(result.PreflightSent);
        Assert.True(result.ActualRequestSent);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(header.ToLowerInvariant(), handler.Requests[0].Headers.GetValues("Access-Control-Request-Headers").Single());
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

    private static string SafeValue(string header) => header == "Accept" ? "text/html" : "en-US";

    private static string UnsafeValue(string header) => header == "Accept" ? "text/html?" : "en_US";
}
