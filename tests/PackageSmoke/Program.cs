using System.Net.Http;
using KeelMatrix.CorsSpec;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
builder.WebHost.UseTestServer();
builder.Services.AddCors(options => options.AddPolicy("smoke", policy => policy.WithOrigins("https://allowed.example")));
var app = builder.Build();
app.UseRouting();
app.UseCors("smoke");
app.MapGet("/orders", () => Results.Ok());
await app.StartAsync();

using var client = app.GetTestClient();
var verifier = new CorsVerifier(client);
var allowed = await verifier.VerifyAsync(new CorsContract(
    new CorsScenario("/orders", "https://allowed.example", HttpMethod.Get),
    CorsExpectation.Allowed()));
var denied = await verifier.VerifyAsync(new CorsContract(
    new CorsScenario("/orders", "https://denied.example", HttpMethod.Get),
    CorsExpectation.Denied()));

if (!allowed.IsSuccess || !denied.IsSuccess)
{
    throw new InvalidOperationException($"Package smoke failed. Allowed: {allowed.Summary} Denied: {denied.Summary}");
}

Console.WriteLine("Package consumer smoke passed: allowed and denied CORS contracts verified.");
await app.DisposeAsync();
