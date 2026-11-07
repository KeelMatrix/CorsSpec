using System.Net.Http;
using KeelMatrix.CorsSpec;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
builder.WebHost.UseTestServer();
builder.Logging.ClearProviders();
var allowedMethods = new[] { "GET", "PATCH" };
var requestedHeaderNames = new[] { "X-Trace" };
builder.Services.AddCors(options => options.AddPolicy("smoke", policy => policy
    .WithOrigins("https://allowed.example")
    .WithMethods(allowedMethods)
    .WithHeaders("X-Trace")));
var app = builder.Build();
app.UseRouting();
var observedAccept = string.Empty;
var observedContentType = string.Empty;
var optionsRequests = 0;
app.Use(async (context, next) =>
{
    if (context.Request.Method == HttpMethod.Options.Method)
    {
        optionsRequests++;
    }

    if (context.Request.Method == HttpMethod.Options.Method &&
        (context.Request.Headers.Accept.Count != 1 || context.Request.Headers.Accept[0] != "*/*"))
    {
        context.Response.StatusCode = StatusCodes.Status406NotAcceptable;
        return;
    }

    await next();
});
app.UseCors("smoke");
var patchRequests = 0;
app.Use(async (context, next) =>
{
    if (context.Request.Method == HttpMethod.Get.Method && context.Request.Path == "/orders")
    {
        observedAccept = context.Request.Headers.Accept.ToString();
        observedContentType = context.Request.Headers.ContentType.ToString();
    }

    if (context.Request.Method == "PATCH")
    {
        patchRequests++;
    }

    await next();
});
app.MapMethods("/orders", allowedMethods, () => Results.Ok());
await app.StartAsync();

using var client = CorsTransport.CreateClient(app.GetTestServer().CreateHandler());
client.BaseAddress = new Uri("http://localhost");
client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "text/html?");
var precedenceVerifier = new CorsVerifier(client, app.GetTestServer().CreateHandler);
var allowed = await precedenceVerifier.VerifyAsync(new CorsContract(
    new CorsScenario("/orders", "https://allowed.example", HttpMethod.Get,
        requestHeaders: new[] { new CorsRequestHeader("Accept", "text/html") }),
    CorsExpectation.Allowed()));

if (!allowed.IsSuccess || allowed.PreflightSent || allowed.ActualRequestSent == false ||
    observedAccept != "text/html" || optionsRequests != 0)
{
    throw new InvalidOperationException($"Package smoke failed for effective request precedence. Allowed: {allowed.Summary} Accept: {observedAccept} OPTIONS: {optionsRequests}.");
}

using var cleanClient = CorsTransport.CreateClient(app.GetTestServer().CreateHandler());
cleanClient.BaseAddress = new Uri("http://localhost");
var verifier = new CorsVerifier(cleanClient, app.GetTestServer().CreateHandler);
var denied = await verifier.VerifyAsync(new CorsContract(
    new CorsScenario("/orders", "https://denied.example", HttpMethod.Get),
    CorsExpectation.Denied()));
if (!denied.IsSuccess)
{
    throw new InvalidOperationException($"Package smoke failed. Denied: {denied.Summary}");
}

var allowedPreflight = await verifier.VerifyAsync(new CorsContract(
    new CorsScenario("/orders", "https://allowed.example", new HttpMethod("PATCH"), requestHeaders: requestedHeaderNames.Select(name => new CorsRequestHeader(name, "smoke"))),
    CorsExpectation.Allowed()));
if (!allowedPreflight.IsSuccess || !allowedPreflight.PreflightSent || !allowedPreflight.ActualRequestSent || patchRequests != 1)
{
    throw new InvalidOperationException($"Package smoke failed for allowed preflight. {allowedPreflight.Summary} PATCH requests: {patchRequests}.");
}

var deniedPreflight = await verifier.VerifyAsync(new CorsContract(
    new CorsScenario("/orders", "https://denied.example", new HttpMethod("PATCH"), requestHeaders: requestedHeaderNames.Select(name => new CorsRequestHeader(name, "smoke"))),
    CorsExpectation.Denied()));
if (!deniedPreflight.IsSuccess || !deniedPreflight.PreflightSent || deniedPreflight.ActualRequestSent || patchRequests != 1)
{
    throw new InvalidOperationException($"Package smoke failed for denied preflight. {deniedPreflight.Summary} PATCH requests: {patchRequests}.");
}


var requestsBeforeUnknown = optionsRequests;
var unknownHeaderNames = new[] { "Accept" };
foreach (var expectation in new[] { CorsExpectation.Allowed(), CorsExpectation.Denied() })
{
    var unknown = await verifier.VerifyAsync(new CorsContract(
        new CorsScenario("/orders", "https://allowed.example", HttpMethod.Get, unknownHeaderNames), expectation));
    if (unknown.IsSuccess || !unknown.Issues.Any(issue => issue.Kind == CorsFailureKind.UnsupportedRequest) ||
        unknown.PreflightSent || unknown.ActualRequestSent)
    {
        throw new InvalidOperationException("Missing effective values must produce an unsupported configuration.");
    }
}
if (optionsRequests != requestsBeforeUnknown) { throw new InvalidOperationException("Unknown values contacted the host."); }

// Direct TestHost would format this raw value to 129 bytes. Neither polarity
// can use a different host representation as evidence about the declared field.
var rawValue = "text/plain;charset=" + new string('a', 109);
foreach (var expectation in new[] { CorsExpectation.Allowed(), CorsExpectation.Denied() })
{
    var unsupported = await verifier.VerifyAsync(new CorsContract(
        new CorsScenario("/orders", "https://allowed.example", HttpMethod.Get,
            requestHeaders: new[] { new CorsRequestHeader("Content-Type", rawValue) }), expectation));
    if (unsupported.IsSuccess || !unsupported.Issues.Any(issue => issue.Kind == CorsFailureKind.UnsupportedRequest) ||
        unsupported.PreflightSent || unsupported.ActualRequestSent)
    {
        throw new InvalidOperationException("TestHost formatting changes must be unsupported before host I/O.");
    }
}

foreach (var length in new[] { 128, 129 })
{
    var value = "text/plain; charset=" + new string('a', length - "text/plain; charset=".Length);
    var content = await verifier.VerifyAsync(new CorsContract(
        new CorsScenario("/orders", "https://allowed.example", HttpMethod.Get,
            requestHeaders: new[] { new CorsRequestHeader("Content-Type", value) }), CorsExpectation.Denied()));
    if (content.PreflightSent != (length == 129) || content.IsSuccess != (length == 129) ||
        (length == 128 && observedContentType != value))
    {
        throw new InvalidOperationException($"Content header length {length}: passed={content.IsSuccess}, preflight={content.PreflightSent}, actual={content.ActualRequestSent}, observedLength={observedContentType.Length}, summary={content.Summary}");
    }
}

await FirstResponseProbes.RunAsync();
await ClientBoundaryProbes.RunAsync();
Console.WriteLine("Package consumer smoke passed: simple and preflight allowed/denied CORS contracts verified.");
await app.DisposeAsync();
