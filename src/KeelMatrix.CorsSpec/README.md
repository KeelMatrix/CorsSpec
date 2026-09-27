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

The package automatically constructs browser-style preflights with `Accept: */*`, normalizes the six browser-standard method names to uppercase, evaluates browser-relevant headers, and reports method, requested-header, credentials, origin, `Vary`, exposed-header, and max-age mismatches. Custom method casing is preserved and custom allow-method tokens must match it exactly. A preflighted `GET`, `HEAD`, or `POST` does not require an allow-method token, but malformed present lists fail closed. The scenario API accepts header names only, so `Accept`, `Accept-Language`, `Content-Language`, `Content-Type`, and `Range` names conservatively force a preflight for both safe and unsafe caller-provided values; include those names for default or per-request handler headers. Browser-safelisted response headers do not need an expose grant, malformed CORS metadata fails closed, and response origin/list values trim only HTTP optional whitespace (`SP` and `HTAB`). It does not own application startup, contact an origin, use browser automation, or prove authentication, authorization, CSRF protection, or other server-side security properties.

Scenario targets must be relative application paths; raw C0 controls (`U+0000`–`U+001F`) and `DEL` (`U+007F`), URI authorities, schemes, backslashes, UNC/device paths, and slash-prefixed root-relative Windows drive paths such as `/C:` or `/C:/orders` are rejected before I/O. Percent-encoded path data remains valid. Browser-forbidden `CONNECT`, `TRACE`, and `TRACK` methods, browser-managed headers, and conditional override headers (`X-HTTP-Method`, `X-HTTP-Method-Override`, and `X-Method-Override`) are also rejected before I/O. `Set-Cookie` and `Set-Cookie2` cannot be asserted as exposed response headers because browsers forbid exposing them.

See the repository [CORS contract guide](https://github.com/KeelMatrix/CorsSpec/blob/main/docs/cors-contracts.md) for the complete usage and caller-owned privacy boundary.

This package targets `.NET 8` (`net8.0`). Verified support for this release line is Windows x64, Linux x64, and macOS with a compatible ASP.NET Core test host. See the repository's [platform support matrix](https://github.com/KeelMatrix/CorsSpec/blob/main/docs/platform-support.md).

CorsSpec's core verification has no independent network activity and remains caller-owned; with a local test host it is fully offline. It sends scenario metadata only through the caller-supplied `HttpClient`. After a response-level verdict, the optional `KeelMatrix.Telemetry` dependency requests one aggregate activation signal per execution (one for a matrix, not per cell). The activation payload is exactly `event`, `tool`, `tool_version`, `telemetry_version`, `schema_version`, pseudonymous `project_hash` and `installation_hash`, `runtime`, `os`, `ci`, and `timestamp`. It never contains origins, hostnames, endpoint paths or identity, endpoint-tied methods, request/response headers, cookies, credentials or tokens, bodies, or diagnostics. Invalid contracts and no-response executions do not request activation; telemetry failure never changes the result. Set `KEELMATRIX_NO_TELEMETRY=1` to suppress it; local and CI validation already do so. See the [repository privacy policy](https://github.com/KeelMatrix/CorsSpec/blob/main/PRIVACY.md) and the [maintained KeelMatrix.Telemetry privacy contract](https://github.com/KeelMatrix/Telemetry/blob/main/app/PRIVACY.md) for storage, queue, network, opt-out, and 90-day retention details.
