# KeelMatrix.CorsSpec

A configured CORS policy is not the same thing as a working browser contract. `KeelMatrix.CorsSpec` sends the request a browser would and checks the headers your ASP.NET Core pipeline actually emits.

## Install

```bash
dotnet add package KeelMatrix.CorsSpec
```

## Quick Start

Give CorsSpec the same `HttpClient` used by your integration tests. The client can be backed by `TestServer`, `WebApplicationFactory`, or another caller-selected handler.

```csharp
using System.Net;
using System.Net.Http;
using KeelMatrix.CorsSpec;

using var client = application.CreateClient();
var contract = new CorsContract(
    new CorsScenario("/api/orders", "https://app.example", HttpMethod.Get),
    CorsExpectation.Allowed());

var result = await new CorsVerifier(client).VerifyAsync(contract);
result.EnsureSuccess();
```

For a preflighted scenario, use a clean handler factory when the actual-request client has defaults or a delegating handler that adds headers:

```csharp
var verifier = new CorsVerifier(testClient, () => testServer.CreateHandler());
var result = await verifier.VerifyAsync(contract);
```

Factory-created preflights use the caller's `HttpClient.Timeout`, so finite timeouts bound preflight completion. `Timeout.InfiniteTimeSpan` remains bounded by an explicit cancellation token; a timeout is reported as `NetworkFailure` and does not send the actual request.

An allowed result requires the response to grant the requested origin. A denied contract passes when browser-relevant CORS headers block that origin; it does not require a particular HTTP status. Set `requireVaryOrigin: true` when the application varies a response by origin and the cache-safety marker is part of the contract.

```csharp
var denied = new CorsContract(
    new CorsScenario("/api/orders", "https://untrusted.example", HttpMethod.Get),
    CorsExpectation.Denied());

(await new CorsVerifier(client).VerifyAsync(denied)).EnsureSuccess();
```

## What is checked

CorsSpec normalizes browser-standard request methods (`DELETE`, `GET`, `HEAD`, `OPTIONS`, `POST`, and `PUT`) to uppercase before simple-method classification and request construction. It constructs a browser-style preflight with `Accept: */*` for non-simple methods or unsafe/unknown requested header values, then executes the actual request only when a 2xx preflight grants access. It interprets `Access-Control-Allow-Origin`, methods, requested headers, credentials, exposed headers, max-age, and `Vary: Origin`. A `204` preflight is not automatically a pass: a missing method or unsafe requested header fails an allowed contract except that a preflighted `GET`, `HEAD`, or `POST` does not require an allow-method match; malformed present lists still fail closed, and a non-2xx preflight always fails an allowed contract. Response allow-method tokens use exact casing; custom method names retain their caller-provided casing and require exact-case matching.

The scenario API accepts declared header names and can model actual values with `CorsRequestHeader`. One effective request representation drives classification, preflight names, and actual-request serialization: an explicit scenario value overrides a same-named `HttpClient.DefaultRequestHeaders` value, while an undeclared default fails closed. CORS safelist classification uses the effective values: safe `Accept`, language, `Content-Type`, and `Range` values remain simple when each normalized value is at most 128 UTF-8 bytes and the effective safelisted values total no more than 1024 UTF-8 bytes; unsafe values and unknown names force a preflight. `Range` accepts only a single ASCII-digit byte range with a nonempty start and a start no greater than its end. `Content-Type` uses MIME parsing with HTTP `SP`/`HTAB` boundaries and HTTP-token type/subtype rules, rather than Unicode whitespace trimming. Supported explicit values are normalized and serialized before I/O, including content headers; unsupported or invalid values fail closed. A factory-created preflight handler must not add caller headers, so `OPTIONS` carries only the browser preflight metadata while the actual request keeps the caller's defaults and handler behavior. Handler-added values cannot be inspected before I/O and remain the caller's responsibility. Factory-created preflights inherit the caller's `HttpClient.Timeout` and cancellation boundary; finite timeout failures are `NetworkFailure` results and do not send the actual request. Response-header assertions honor the browser safelist (`Cache-Control`, `Content-Language`, `Content-Length`, `Content-Type`, `Expires`, `Last-Modified`, and `Pragma`) and fail closed on malformed CORS metadata; absent, valid-empty, wildcard, and explicit-member response lists are evaluated according to their field-specific rules. Origin and list values trim only HTTP optional whitespace (`SP` and `HTAB`).

Scenario targets must be relative application paths; raw C0 controls (`U+0000`–`U+001F`) and `DEL` (`U+007F`), URI authorities and schemes at the reference start, backslashes, UNC/device paths, and every slash-prefixed root-relative Windows drive-shaped path such as `/C:`, `/C:/orders`, or `/C:orders` are rejected before I/O. Percent-encoded path data remains valid, and raw scheme-like text in query or fragment data such as `?next=https://external.example/orders` or `?next=/C:orders` remains valid. Browser-forbidden `CONNECT`, `TRACE`, and `TRACK` methods, browser-managed headers, and conditional override headers (`X-HTTP-Method`, `X-HTTP-Method-Override`, and `X-Method-Override`) are also rejected before I/O. `Set-Cookie` and `Set-Cookie2` cannot be asserted as exposed response headers because browsers forbid exposing them.

Use `CorsMatrix` for a small set of contracts:

```csharp
var matrix = new CorsMatrix(new[] { contract, denied });
var results = await new CorsVerifier(client).VerifyMatrixAsync(matrix);
```

See [docs/cors-contracts.md](docs/cors-contracts.md) for preflight construction, middleware and endpoint policies, credentials, diagnostics, the caller-owned network boundary, and the full limitation statement.

## Compatibility

The package targets `.NET 8` (`net8.0`). Verified CI coverage for this release line is the GitHub-hosted Windows Server 2025 x64 runner (`windows-2025`), Linux x64, and macOS with a compatible ASP.NET Core test host; this does not claim a specific Windows 10/11 consumer host. See the [platform support matrix](docs/platform-support.md).

## Important limitation

CORS is a browser cross-origin policy mechanism, not authentication or authorization. A passing CorsSpec contract does not prove that an endpoint is protected from non-browser callers, protected from CSRF, safe from server-side request forgery, or protected by a network firewall. CorsSpec does not implement middleware, repair policies, automate a browser, or contact an origin.

## Privacy

CorsSpec's core verification has no independent network activity and remains caller-owned; with a local test host it is fully offline. It sends scenario metadata only through the caller-supplied `HttpClient`. After an individual contract reaches a response-level verdict, CorsSpec calls the parameterless `TrackActivation()` method. Invalid contracts and operations without a response are ineligible, and no CORS contract data is supplied to telemetry. The shared package owns payload, deduplication, opt-out, identity/state, queueing, delivery, and failure handling. See [PRIVACY.md](PRIVACY.md) and the [maintained KeelMatrix.Telemetry privacy contract](https://github.com/KeelMatrix/Telemetry/blob/main/PRIVACY.md); local and CI validation suppress telemetry through the shared opt-out.

## License

MIT. See [LICENSE](LICENSE).
