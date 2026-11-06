using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;

namespace KeelMatrix.CorsSpec.IntegrationTests;

public sealed class TestHostRepresentationTests
{
    public static IEnumerable<object[]> Headers()
    {
        foreach (var allowed in new[] { false, true })
        {
            foreach (var optionsGrant in new[] { false, true })
            {
                yield return ["Content-Type", "text/plain;charset=" + new string('a', 109), false, false, allowed, optionsGrant];
                yield return ["Content-Type", "text/plain; charset=" + new string('a', 108), true, false, allowed, optionsGrant];
                yield return ["Content-Type", "text/plain; charset=" + new string('a', 109), true, true, allowed, optionsGrant];
                yield return ["Accept", "text/html;q=0.9", false, false, allowed, optionsGrant];
                yield return ["Accept", "text/html; q=0.9", true, false, allowed, optionsGrant];
                yield return ["Accept-Language", "en-US,en;q=0.9", false, false, allowed, optionsGrant];
                yield return ["Accept-Language", "en-US, en; q=0.9", false, false, allowed, optionsGrant];
                yield return ["Content-Language", "en-US,fr", false, false, allowed, optionsGrant];
                yield return ["Content-Language", "en-US, fr", false, false, allowed, optionsGrant];
                yield return ["Content-Disposition", "attachment;filename=a", false, true, allowed, optionsGrant];
                yield return ["Content-Disposition", "attachment; filename=a", true, true, allowed, optionsGrant];
                yield return ["Content-Encoding", "gzip,br", false, true, allowed, optionsGrant];
                yield return ["Content-Encoding", "gzip, br", false, true, allowed, optionsGrant];
                yield return ["Accept-Language", "en-US", true, false, allowed, optionsGrant];
                yield return ["Range", " \tbytes=0-1\t ", true, false, allowed, optionsGrant];
            }
        }
    }

    [Theory]
    [MemberData(nameof(Headers))]
    public async Task TestHost_must_preserve_the_classified_representation_or_report_unsupported(
        string name, string value, bool supported, bool preflight, bool allowed, bool optionsGrant)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        await using var app = builder.Build();
        var observed = new List<(string Method, string?[] Values)>();
        app.Run(context =>
        {
            var options = context.Request.Method == "OPTIONS";
            observed.Add((context.Request.Method, context.Request.Headers[name].ToArray()));
            // Opposite grants expose artificial preflight and skipped-preflight mistakes.
            if (options ? optionsGrant : !optionsGrant)
            {
                context.Response.Headers["Access-Control-Allow-Origin"] = "https://app.example";
                context.Response.Headers["Access-Control-Allow-Headers"] = name;
                context.Response.Headers.Vary = "Origin";
            }
            context.Response.StatusCode = 204;
            return Task.CompletedTask;
        });
        await app.StartAsync();
        using var client = CorsTransport.CreateClient(app.GetTestServer().CreateHandler());
        client.BaseAddress = new Uri("http://localhost");
        var result = await new CorsVerifier(client).VerifyAsync(new CorsContract(
            new CorsScenario("/orders", "https://app.example", HttpMethod.Get,
                requestHeaders: [new CorsRequestHeader(name, value)]),
            allowed ? CorsExpectation.Allowed() : CorsExpectation.Denied()));

        if (!supported)
        {
            Assert.False(result.IsSuccess);
            Assert.Contains(result.Issues, issue => issue.Kind == CorsFailureKind.UnsupportedRequest);
            Assert.Empty(observed);
            return;
        }

        Assert.Equal(preflight, result.PreflightSent);
        Assert.Equal(allowed == (!preflight && !optionsGrant), result.IsSuccess);
        if (!preflight || optionsGrant)
        {
            Assert.Equal(value.Trim(' ', '\t'), Assert.Single(observed.Last().Values));
        }
    }
}
