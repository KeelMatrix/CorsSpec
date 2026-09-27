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

An allowed result requires the response to grant the requested origin. A denied contract passes when browser-relevant CORS headers block that origin; it does not require a particular HTTP status. Set `requireVaryOrigin: true` when the application varies a response by origin and the cache-safety marker is part of the contract.

```csharp
var denied = new CorsContract(
    new CorsScenario("/api/orders", "https://untrusted.example", HttpMethod.Get),
    CorsExpectation.Denied());

(await new CorsVerifier(client).VerifyAsync(denied)).EnsureSuccess();
```

## What is checked

CorsSpec normalizes case-insensitive browser methods (`DELETE`, `GET`, `HEAD`, `OPTIONS`, `POST`, and `PUT`) to uppercase before simple-method classification and request construction. It constructs a browser-style preflight with `Accept: */*` for non-simple methods or requested headers, then executes the actual request only when a 2xx preflight grants access. It interprets `Access-Control-Allow-Origin`, methods, requested headers, credentials, exposed headers, max-age, and `Vary: Origin`. A `204` preflight is not automatically a pass: a missing method or requested header fails an allowed contract except that a preflighted `GET`, `HEAD`, or `POST` does not require an allow-method match; malformed present lists still fail closed, and a non-2xx preflight always fails an allowed contract. Other valid custom method names retain their caller-provided casing and require exact-case allow-method matching.

The scenario API accepts header names, not values. To avoid a name-only false negative, every requested caller-added header conservatively forces a preflight, including `Accept`, `Accept-Language`, `Content-Language`, `Content-Type`, and `Range`; list those names for both default and per-request handler headers. Response-header assertions honor the browser safelist (`Cache-Control`, `Content-Language`, `Content-Length`, `Content-Type`, `Expires`, `Last-Modified`, and `Pragma`) and fail closed on malformed CORS metadata; origin and list values trim only HTTP optional whitespace (`SP` and `HTAB`).

Scenario targets must be relative application paths; raw C0 controls (`U+0000`–`U+001F`) and `DEL` (`U+007F`), URI authorities, schemes, backslashes, UNC/device paths, and slash-prefixed root-relative Windows drive paths such as `/C:` or `/C:/orders` are rejected before I/O. Percent-encoded path data remains valid. Browser-forbidden `CONNECT`, `TRACE`, and `TRACK` methods, browser-managed headers, and conditional override headers (`X-HTTP-Method`, `X-HTTP-Method-Override`, and `X-Method-Override`) are also rejected before I/O. `Set-Cookie` and `Set-Cookie2` cannot be asserted as exposed response headers because browsers forbid exposing them.

Use `CorsMatrix` for a small set of contracts:

```csharp
var matrix = new CorsMatrix(new[] { allowed, denied });
var results = await new CorsVerifier(client).VerifyMatrixAsync(matrix);
```

See [docs/cors-contracts.md](docs/cors-contracts.md) for preflight construction, middleware and endpoint policies, credentials, diagnostics, the caller-owned network boundary, and the full limitation statement.

## Compatibility

The package targets `.NET 8` (`net8.0`). Verified support for this release line is Windows x64, Linux x64, and macOS with a compatible ASP.NET Core test host; see the [platform support matrix](docs/platform-support.md).

## Important limitation

CORS is a browser cross-origin policy mechanism, not authentication or authorization. A passing CorsSpec contract does not prove that an endpoint is protected from non-browser callers, protected from CSRF, safe from server-side request forgery, or protected by a network firewall. CorsSpec does not implement middleware, repair policies, automate a browser, or contact an origin.

## Privacy

CorsSpec's core verification remains offline and caller-owned: it intentionally sends the scenario path, `Origin`, and preflight metadata only through the caller-supplied `HttpClient`. After a contract reaches a response-level CORS verdict, an optional best-effort activation signal may leave the process through `KeelMatrix.Telemetry`; it is aggregate and contains no origin, hostname, path, method identity, header values, cookies, tokens, response bodies, or diagnostics. Matrix execution requests one signal for the execution, not one per cell. Set `KEELMATRIX_NO_TELEMETRY=1` to suppress it; local and CI validation already do so. See [PRIVACY.md](PRIVACY.md).

## License

MIT. See [LICENSE](LICENSE).
