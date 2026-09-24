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

CorsSpec constructs a preflight for non-simple methods or requested headers, then executes the actual request when the preflight grants access. It interprets `Access-Control-Allow-Origin`, methods, requested headers, credentials, exposed headers, max-age, and `Vary: Origin`. A `204` preflight is not automatically a pass: a missing method or requested header still fails an allowed contract.

Use `CorsMatrix` for a small set of contracts:

```csharp
var matrix = new CorsMatrix(new[] { allowed, denied });
var results = await new CorsVerifier(client).VerifyMatrixAsync(matrix);
```

See [docs/cors-contracts.md](docs/cors-contracts.md) for preflight construction, middleware and endpoint policies, credentials, diagnostics, and the full limitation statement.

## Important limitation

CORS is a browser cross-origin policy mechanism, not authentication or authorization. A passing CorsSpec contract does not prove that an endpoint is protected from non-browser callers, protected from CSRF, safe from server-side request forgery, or protected by a network firewall. CorsSpec does not implement middleware, repair policies, automate a browser, or contact an origin.

## Privacy

CorsSpec has no telemetry. It never sends origins, paths, headers, cookies, tokens, response bodies, or diagnostics to a service. See [PRIVACY.md](PRIVACY.md).

## License

MIT. See [LICENSE](LICENSE).
