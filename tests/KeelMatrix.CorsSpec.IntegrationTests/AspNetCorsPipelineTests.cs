using System.Net.Http;
using KeelMatrix.CorsSpec;

namespace KeelMatrix.CorsSpec.IntegrationTests;

public sealed class AspNetCorsPipelineTests
{
    [Fact]
    public async Task Global_policy_allows_a_simple_request()
    {
        await using var host = await TestCorsHost.CreateAsync(CorsHostMode.Global);
        var contract = new CorsContract(
            new CorsScenario("/orders", "https://global.example", HttpMethod.Get),
            CorsExpectation.Allowed());

        var result = await new CorsVerifier(host.Client).VerifyAsync(contract);

        Assert.True(result.IsSuccess, result.Summary);
        Assert.False(result.PreflightSent);
        Assert.True(result.ActualRequestSent);
    }

    [Fact]
    public async Task Global_policy_denies_an_origin_without_requiring_a_failure_status()
    {
        await using var host = await TestCorsHost.CreateAsync(CorsHostMode.Global);
        var contract = new CorsContract(
            new CorsScenario("/orders", "https://untrusted.example", HttpMethod.Get),
            CorsExpectation.Denied());

        var result = await new CorsVerifier(host.Client).VerifyAsync(contract);

        Assert.True(result.IsSuccess, result.Summary);
        Assert.Equal(System.Net.HttpStatusCode.OK, result.ActualStatusCode);
    }

    [Fact]
    public async Task Global_policy_allows_a_custom_method_and_requested_header_preflight()
    {
        await using var host = await TestCorsHost.CreateAsync(CorsHostMode.Global);
        var contract = new CorsContract(
            new CorsScenario("/orders", "https://global.example", HttpMethod.Delete, new[] { "X-Trace" }),
            CorsExpectation.Allowed(expectedMaxAge: TimeSpan.FromMinutes(10), expectedExposedHeaders: new[] { "X-Request-Id" }));

        var result = await new CorsVerifier(host.Client).VerifyAsync(contract);

        Assert.True(result.IsSuccess, result.Summary);
        Assert.True(result.PreflightSent);
        Assert.True(result.ActualRequestSent);
    }

    [Fact]
    public async Task Global_policy_denies_a_preflight_method_even_when_framework_returns_no_content()
    {
        await using var host = await TestCorsHost.CreateAsync(CorsHostMode.Global);
        var contract = new CorsContract(
            new CorsScenario("/orders", "https://global.example", HttpMethod.Put),
            CorsExpectation.Allowed());

        var result = await new CorsVerifier(host.Client).VerifyAsync(contract);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Kind == CorsFailureKind.MethodRejected);
        Assert.False(result.ActualRequestSent);
    }

    [Fact]
    public async Task Named_endpoint_policy_is_observed_through_endpoint_metadata()
    {
        await using var host = await TestCorsHost.CreateAsync(CorsHostMode.Endpoint);
        var contract = new CorsContract(
            new CorsScenario("/named", "https://named.example", HttpMethod.Get),
            CorsExpectation.Allowed());

        var result = await new CorsVerifier(host.Client).VerifyAsync(contract);

        Assert.True(result.IsSuccess, result.Summary);
    }

    [Fact]
    public async Task Endpoint_without_cors_metadata_remains_denied_when_metadata_middleware_is_used()
    {
        await using var host = await TestCorsHost.CreateAsync(CorsHostMode.Endpoint);
        var contract = new CorsContract(
            new CorsScenario("/plain", "https://named.example", HttpMethod.Get),
            CorsExpectation.Denied());

        var result = await new CorsVerifier(host.Client).VerifyAsync(contract);

        Assert.True(result.IsSuccess, result.Summary);
    }

    [Fact]
    public async Task Credentialed_policy_emits_credentials_exposed_headers_and_vary()
    {
        await using var host = await TestCorsHost.CreateAsync(CorsHostMode.Credentials);
        var contract = new CorsContract(
            new CorsScenario("/orders", "https://credentialed.example", HttpMethod.Get, useCredentials: true),
            CorsExpectation.Allowed(expectedExposedHeaders: new[] { "X-Request-Id" }));

        var result = await new CorsVerifier(host.Client).VerifyAsync(contract);

        Assert.True(result.IsSuccess, result.Summary);
    }

    [Fact]
    public async Task Wildcard_policy_is_accepted_when_the_contract_explicitly_allows_it()
    {
        await using var host = await TestCorsHost.CreateAsync(CorsHostMode.Wildcard);
        var contract = new CorsContract(
            new CorsScenario("/orders", "https://any.example", HttpMethod.Get),
            CorsExpectation.Allowed(allowWildcardOrigin: true));

        var result = await new CorsVerifier(host.Client).VerifyAsync(contract);

        Assert.True(result.IsSuccess, result.Summary);
    }

    [Fact]
    public async Task Wildcard_policy_does_not_satisfy_a_denied_origin_contract()
    {
        await using var host = await TestCorsHost.CreateAsync(CorsHostMode.Wildcard);
        var contract = new CorsContract(
            new CorsScenario("/orders", "https://untrusted.example", HttpMethod.Get),
            CorsExpectation.Denied());

        var result = await new CorsVerifier(host.Client).VerifyAsync(contract);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Kind == CorsFailureKind.UnexpectedCorsPermission);
    }
}
