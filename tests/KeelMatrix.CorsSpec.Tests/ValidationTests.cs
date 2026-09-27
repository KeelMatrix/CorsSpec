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
        Assert.Throws<ArgumentException>(() => new CorsScenario("//other-host/orders", "https://app.example", HttpMethod.Get));
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
    [InlineData("Accept")]
    [InlineData("Accept-Language")]
    [InlineData("Content-Language")]
    [InlineData("Content-Type")]
    [InlineData("Range")]
    public void Value_sensitive_header_names_without_values_conservatively_force_preflight(string header)
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
    public void Browser_managed_and_cors_protocol_headers_are_rejected(string header)
    {
        Assert.Throws<ArgumentException>(() => new CorsScenario("/orders", "https://app.example", HttpMethod.Get, new[] { header }));
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
