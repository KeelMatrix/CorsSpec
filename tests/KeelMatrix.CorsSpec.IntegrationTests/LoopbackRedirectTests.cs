using System.Net;
using System.Net.Http;
using KeelMatrix.CorsSpec;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace KeelMatrix.CorsSpec.IntegrationTests;

public sealed class LoopbackRedirectTests
{
    [Fact]
    public async Task Actual_same_origin_redirect_is_rejected_with_real_httpclienthandler()
    {
        await using var host = await LoopbackCorsHost.CreateAsync();
        using var client = CreateClient(host.BaseAddress);

        var result = await new CorsVerifier(client).VerifyAsync(new CorsContract(
            new CorsScenario("/same-redirect", "https://app.example", HttpMethod.Get),
            CorsExpectation.Allowed()));

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Kind == CorsFailureKind.RedirectNotSupported);
    }

    [Fact]
    public async Task Preflight_redirect_is_rejected_with_the_one_argument_constructor()
    {
        await using var host = await LoopbackCorsHost.CreateAsync();
        using var client = CreateClient(host.BaseAddress);

        var result = await new CorsVerifier(client).VerifyAsync(new CorsContract(
            new CorsScenario("/preflight-redirect", "https://app.example", HttpMethod.Delete),
            CorsExpectation.Allowed()));

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Kind == CorsFailureKind.RedirectNotSupported);
        Assert.False(result.ActualRequestSent);
    }

    [Fact]
    public async Task Method_changing_actual_redirect_is_rejected_with_a_factory_preflight()
    {
        await using var host = await LoopbackCorsHost.CreateAsync();
        using var client = CreateClient(host.BaseAddress);

        var result = await new CorsVerifier(client, () => new HttpClientHandler { AllowAutoRedirect = true, UseProxy = false })
            .VerifyAsync(new CorsContract(
                new CorsScenario("/method-changing-redirect", "https://app.example", HttpMethod.Delete),
                CorsExpectation.Allowed()));

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Kind == CorsFailureKind.RedirectNotSupported);
        Assert.True(result.PreflightSent);
        Assert.True(result.ActualRequestSent);
    }

    [Fact]
    public async Task Cross_origin_actual_redirect_is_rejected_before_terminal_cors_evaluation()
    {
        await using var destination = await LoopbackCorsHost.CreateAsync();
        await using var source = await LoopbackCorsHost.CreateAsync(destination.BaseAddress);
        using var client = CreateClient(source.BaseAddress);

        var result = await new CorsVerifier(client).VerifyAsync(new CorsContract(
            new CorsScenario("/cross-redirect", "https://app.example", HttpMethod.Get),
            CorsExpectation.Denied()));

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Kind == CorsFailureKind.RedirectNotSupported);
    }

    [Fact]
    public async Task Not_modified_is_evaluated_as_a_non_redirect_response()
    {
        await using var host = await LoopbackCorsHost.CreateAsync();
        using var client = CreateClient(host.BaseAddress);

        var result = await new CorsVerifier(client).VerifyAsync(new CorsContract(
            new CorsScenario("/not-modified", "https://app.example", HttpMethod.Get),
            CorsExpectation.Allowed()));

        Assert.True(result.IsSuccess, result.Summary);
        Assert.Equal(HttpStatusCode.NotModified, result.ActualStatusCode);
        Assert.DoesNotContain(result.Issues, issue => issue.Kind == CorsFailureKind.RedirectNotSupported);
    }

    private static HttpClient CreateClient(Uri baseAddress) => new(new HttpClientHandler
    {
        AllowAutoRedirect = true,
        UseProxy = false
    })
    {
        BaseAddress = baseAddress
    };
}

internal sealed class LoopbackCorsHost : IAsyncDisposable
{
    private readonly WebApplication _application;

    private LoopbackCorsHost(WebApplication application, Uri baseAddress)
    {
        _application = application;
        BaseAddress = baseAddress;
    }

    public Uri BaseAddress { get; }

    public static async Task<LoopbackCorsHost> CreateAsync(Uri? crossRedirectTarget = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
        var app = builder.Build();

        app.Use(async (context, next) =>
        {
            context.Response.Headers["Access-Control-Allow-Origin"] = "https://app.example";
            context.Response.Headers["Access-Control-Allow-Methods"] = "DELETE";
            context.Response.Headers["Access-Control-Allow-Headers"] = "Authorization, X-Trace";
            context.Response.Headers["Vary"] = "Origin";
            await next();
        });

        app.MapMethods("/orders", new[] { "OPTIONS" }, () => Results.NoContent());
        app.MapMethods("/orders", new[] { "DELETE" }, () => Results.NoContent());
        app.MapGet("/same-redirect", () => Results.Redirect("/final"));
        app.MapMethods("/preflight-redirect", new[] { "OPTIONS" }, () => Results.Redirect("/final"));
        app.MapMethods("/method-changing-redirect", new[] { "OPTIONS" }, () => Results.NoContent());
        app.MapMethods("/method-changing-redirect", new[] { "DELETE" }, () => Results.Redirect("/final"));
        app.MapGet("/cross-redirect", () => Results.Redirect(new Uri(crossRedirectTarget ?? new Uri("http://127.0.0.1/"), "/final").ToString()));
        app.MapGet("/not-modified", context =>
        {
            context.Response.StatusCode = StatusCodes.Status304NotModified;
            return Task.CompletedTask;
        });
        app.MapGet("/final", () => Results.Ok());

        await app.StartAsync();
        var address = app.Urls
            .Select(static value => new Uri(value))
            .Single(static value => IPAddress.TryParse(value.Host, out var address) && IPAddress.IsLoopback(address));
        return new LoopbackCorsHost(app, address);
    }

    public async ValueTask DisposeAsync() => await _application.DisposeAsync();
}
