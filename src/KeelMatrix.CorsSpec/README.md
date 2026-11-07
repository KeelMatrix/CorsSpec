# KeelMatrix.CorsSpec

`KeelMatrix.CorsSpec` verifies the CORS headers emitted by an application through a caller-supplied `HttpClient`.

Install it with:

```bash
dotnet add package KeelMatrix.CorsSpec
```

Create the shared integration-test client once through `CorsTransport.CreateClient` over the existing host's direct handler. Use it for ordinary tests and `CorsVerifier`; you own and dispose the client, and application startup stays in your test setup. For `WebApplicationFactory`, use `factory.Server.CreateHandler()`.

```csharp
using KeelMatrix.CorsSpec;
using System.Net.Http;

using var testClient = CorsTransport.CreateClient(testServer.CreateHandler());
testClient.BaseAddress = new Uri("http://localhost");
using var ordinaryResponse = await testClient.GetAsync("/api/orders");
var contract = new CorsContract(
    new CorsScenario("/api/orders", "https://app.example", HttpMethod.Get),
    CorsExpectation.Allowed());

var result = await new CorsVerifier(testClient).VerifyAsync(contract);
result.EnsureSuccess();
```

For a preflighted scenario, use a clean handler factory when the actual-request client has defaults or a delegating handler that adds headers:

```csharp
var verifier = new CorsVerifier(testClient, () => testServer.CreateHandler());
var result = await verifier.VerifyAsync(contract);
```

Factory-created preflights use the caller's `HttpClient.Timeout`, so finite timeouts bound preflight completion. `Timeout.InfiniteTimeSpan` remains bounded by an explicit cancellation token; a timeout is reported as `NetworkFailure` and does not send the actual request.

Already-created ordinary clients from `TestServer.CreateClient()`, `WebApplicationFactory.CreateClient()`, or `new HttpClient` return actionable `UnsupportedTransport` before verification host I/O. Change client creation once, then configure `BaseAddress`, defaults and timeout as usual. Factory client settings and automatic redirect/cookie handlers do not transfer to the direct handler; supported network handlers must disable automatic redirects. This version supplies no host-observer adapter. Supported delegating handlers must meet the deterministic preparation contract in the [contract guide](https://github.com/KeelMatrix/CorsSpec/blob/main/docs/cors-contracts.md).

Declare author header names or model values with `CorsRequestHeader`. The client must come from `CorsTransport.CreateClient`: preparation observes defaults and handler-added values without host I/O, then the final boundary checks the same method, target, origin, normalized values and encoding. Missing values or unsupported transports cannot become a successful denial. Explicit values override defaults. Supported terminal handlers are direct Microsoft TestHost `ClientHandler` and exact `HttpClientHandler`/`SocketsHttpHandler`; network handlers must disable automatic redirects, authentication and decompression. Ordinary unregistered clients and other opaque terminals are unsupported. Direct TestHost headers must retain one identical value after .NET parsing; formatting or splitting changes return `UnsupportedRequest`. Use canonical single values or a supported network handler for raw fields.

Generated preflight names are sorted lowercase and comma-only. A clean preflight factory keeps caller defaults and credentials off `OPTIONS`, inherits the client timeout/cancellation boundary, and must satisfy the same transport contract. Verdicts use an immutable snapshot of the first response's status, CORS headers and request provenance; later handler mutations cannot change that evidence. First redirect responses are unsupported for both expectations, including self-redirects; terminal URI equality alone is not redirect evidence. Cookies are browser-managed credentials, while `Authorization` is a declarable author header. The [contract guide](https://github.com/KeelMatrix/CorsSpec/blob/main/docs/cors-contracts.md) maintains encoding, 128/1024-byte limits, handler preparation, missing-value behavior and migration instructions.

Scenario targets must be relative application paths; raw C0 controls (`U+0000`–`U+001F`) and `DEL` (`U+007F`), URI authorities and schemes at the reference start, backslashes, UNC/device paths, and every slash-prefixed root-relative Windows drive-shaped path such as `/C:`, `/C:/orders`, or `/C:orders` are rejected before I/O. Percent-encoded path data remains valid, and raw scheme-like text in query or fragment data such as `?next=https://external.example/orders` or `?next=/C:orders` remains valid. Browser-forbidden `CONNECT`, `TRACE`, and `TRACK` methods, browser-managed headers, and conditional override headers (`X-HTTP-Method`, `X-HTTP-Method-Override`, and `X-Method-Override`) are also rejected before I/O. `Set-Cookie` and `Set-Cookie2` cannot be asserted as exposed response headers because browsers forbid exposing them.

See the repository [CORS contract guide](https://github.com/KeelMatrix/CorsSpec/blob/main/docs/cors-contracts.md) for the complete usage and caller-owned privacy boundary.

This package targets `.NET 8` (`net8.0`). Verified CI coverage for this release line is the GitHub-hosted Windows Server 2025 x64 runner (`windows-2025`), Linux x64, and macOS with a compatible ASP.NET Core test host; this does not claim a specific Windows 10/11 consumer host. See the repository's [platform support matrix](https://github.com/KeelMatrix/CorsSpec/blob/main/docs/platform-support.md).

CorsSpec's core verification has no independent network activity and remains caller-owned; with a local test host it is fully offline. It sends scenario metadata only through the caller-supplied `HttpClient`. After a single-contract verdict, or once for a completed matrix with at least one eligible verdict, CorsSpec calls the parameterless `TrackActivation()` method. Failed CORS verdicts are eligible; invalid/unsupported configurations, no-response failures and matrices that propagate cancellation are ineligible, and no CORS contract data is supplied to telemetry. The shared package owns payload, deduplication, opt-out, identity/state, queueing, delivery, and failure handling. See the [repository privacy policy](https://github.com/KeelMatrix/CorsSpec/blob/main/PRIVACY.md) and the [maintained KeelMatrix.Telemetry privacy contract](https://github.com/KeelMatrix/Telemetry/blob/main/PRIVACY.md); local and CI validation suppress telemetry through the shared opt-out.
