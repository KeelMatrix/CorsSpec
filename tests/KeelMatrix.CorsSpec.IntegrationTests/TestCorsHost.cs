using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace KeelMatrix.CorsSpec.IntegrationTests;

internal sealed class TestCorsHost : IAsyncDisposable
{
    private readonly WebApplication _application;
    private readonly Func<int> _preflightObservations;
    private readonly Func<int> _credentialedPreflightRejections;

    private TestCorsHost(WebApplication application, Func<int> preflightObservations, Func<int> credentialedPreflightRejections)
    {
        _application = application;
        _preflightObservations = preflightObservations;
        _credentialedPreflightRejections = credentialedPreflightRejections;
        Client = application.GetTestClient();
    }

    public HttpClient Client { get; }

    public int PreflightObservations => _preflightObservations();

    public int CredentialedPreflightRejections => _credentialedPreflightRejections();

    public HttpMessageHandler CreateHandler() => _application.GetTestServer().CreateHandler();

    public static async Task<TestCorsHost> CreateAsync(CorsHostMode mode, bool rejectCredentialedPreflights = false)
    {
        var preflightObservations = 0;
        var credentialedPreflightRejections = 0;
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.WebHost.UseTestServer();
        builder.Services.AddCors(options =>
        {
            options.AddPolicy("global", policy => policy
                .WithOrigins("https://global.example")
                .WithMethods("GET", "DELETE")
                .WithHeaders("X-Trace")
                .WithExposedHeaders("X-Request-Id")
                .SetPreflightMaxAge(TimeSpan.FromMinutes(10)));
            options.AddPolicy("named", policy => policy
                .WithOrigins("https://named.example")
                .WithMethods("GET")
                .AllowAnyHeader());
            options.AddPolicy("credentials", policy => policy
                .WithOrigins("https://credentialed.example")
                .WithMethods("GET", "DELETE")
                .WithHeaders("X-Trace", "Authorization")
                .AllowCredentials()
                .WithExposedHeaders("X-Request-Id")
                .SetPreflightMaxAge(TimeSpan.FromMinutes(10)));
            options.AddPolicy("wildcard", policy => policy.AllowAnyOrigin());
        });

        var app = builder.Build();
        app.UseRouting();
        app.Use(async (context, next) =>
        {
            if (context.Request.Method == HttpMethod.Options.Method)
            {
                preflightObservations++;
                var hasCallerCredentialOrCustomHeader = context.Request.Headers.Authorization.Count != 0 ||
                    context.Request.Headers.Cookie.Count != 0 ||
                    context.Request.Headers.ContainsKey("X-Trace");
                if (rejectCredentialedPreflights && hasCallerCredentialOrCustomHeader)
                {
                    credentialedPreflightRejections++;
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    return;
                }

                if (context.Request.Headers.Accept.Count != 1 || context.Request.Headers.Accept[0] != "*/*")
                {
                    context.Response.StatusCode = StatusCodes.Status406NotAcceptable;
                    return;
                }
            }

            await next();
        });
        switch (mode)
        {
            case CorsHostMode.Global:
                app.UseCors("global");
                break;
            case CorsHostMode.Endpoint:
                app.UseCors();
                break;
            case CorsHostMode.Credentials:
                app.UseCors("credentials");
                break;
            case CorsHostMode.Wildcard:
                app.UseCors("wildcard");
                break;
        }

        app.MapGet("/orders", () => Results.Ok(new { status = "ok" }));
        app.MapMethods("/orders", new[] { "DELETE" }, () => Results.NoContent());
        if (mode == CorsHostMode.Endpoint)
        {
            app.MapGet("/named", () => Results.Ok()).RequireCors("named");
            app.MapGet("/plain", () => Results.Ok());
        }

        await app.StartAsync();
        return new TestCorsHost(app, () => preflightObservations, () => credentialedPreflightRejections);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _application.DisposeAsync();
    }
}

internal enum CorsHostMode
{
    Global,
    Endpoint,
    Credentials,
    Wildcard
}
