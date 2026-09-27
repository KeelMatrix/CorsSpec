# KeelMatrix.CorsSpec

`KeelMatrix.CorsSpec` verifies the CORS headers emitted by an application through a caller-supplied `HttpClient`.

Install it with:

```bash
dotnet add package KeelMatrix.CorsSpec
```

```csharp
var contract = new CorsContract(
    new CorsScenario("/api/orders", "https://app.example", HttpMethod.Get),
    CorsExpectation.Allowed());

var result = await new CorsVerifier(testClient).VerifyAsync(contract);
result.EnsureSuccess();
```

The package automatically constructs meaningful preflights, normalizes the six browser-standard method names to uppercase, evaluates browser-relevant headers, and reports method, requested-header, credentials, origin, `Vary`, exposed-header, and max-age mismatches. Custom method casing is preserved. The scenario API accepts header names only, so `Accept`, `Accept-Language`, `Content-Language`, `Content-Type`, and `Range` names conservatively force a preflight for both safe and unsafe caller-provided values; include those names for default or per-request handler headers. Browser-safelisted response headers do not need an expose grant, and malformed CORS metadata fails closed. It does not own application startup, contact an origin, use browser automation, or prove authentication, authorization, CSRF protection, or other server-side security properties.

Scenario targets must be relative application paths; raw C0 controls (`U+0000`–`U+001F`) and `DEL` (`U+007F`), URI authorities, schemes, backslashes, UNC/device paths, and root-relative Windows paths are rejected before I/O. Percent-encoded path data remains valid. Browser-forbidden `CONNECT`, `TRACE`, and `TRACK` methods, browser-managed headers, and conditional override headers (`X-HTTP-Method`, `X-HTTP-Method-Override`, and `X-Method-Override`) are also rejected before I/O. `Set-Cookie` and `Set-Cookie2` cannot be asserted as exposed response headers because browsers forbid exposing them.

See the repository [CORS contract guide](https://github.com/KeelMatrix/CorsSpec/blob/main/docs/cors-contracts.md) for the complete usage and caller-owned privacy boundary.

This package targets `.NET 8` (`net8.0`). Verified support for this release line is Windows x64, Linux x64, and macOS with a compatible ASP.NET Core test host. See the repository's [platform support matrix](https://github.com/KeelMatrix/CorsSpec/blob/main/docs/platform-support.md).

CorsSpec has no telemetry or independent network activity. It sends the scenario request through the `HttpClient` supplied by the caller, so that client controls network behavior; CorsSpec does not add credentials or transmit response data to a CorsSpec service.
