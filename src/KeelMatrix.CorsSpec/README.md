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

For a preflighted scenario, use a clean handler factory when the actual-request client has defaults or a delegating handler that adds headers:

```csharp
var verifier = new CorsVerifier(testClient, () => testServer.CreateHandler());
var result = await verifier.VerifyAsync(contract);
```

Factory-created preflights use the caller's `HttpClient.Timeout`, so finite timeouts bound preflight completion. `Timeout.InfiniteTimeSpan` remains bounded by an explicit cancellation token; a timeout is reported as `NetworkFailure` and does not send the actual request.

The package automatically constructs isolated browser-style preflights with `Accept: */*`, normalizes the six browser-standard request method names to uppercase, evaluates browser-relevant headers, and reports method, requested-header, credentials, origin, `Vary`, exposed-header, and max-age mismatches. Custom method casing is preserved and response allow-method tokens must match it exactly. A preflighted `GET`, `HEAD`, or `POST` does not require an allow-method token, but malformed present lists fail closed. The scenario API accepts declared names and can model values with `CorsRequestHeader`; browser safelist classification keeps safe `Accept`, language, `Content-Type`, and `Range` values simple while unsafe values and unknown names force a preflight. Explicit default-header names must be declared, and handler-added values cannot be inspected before I/O. Use the clean factory for clients with default or handler-added headers so actual-request headers remain on the actual request only. Factory-created preflights use the caller's `HttpClient.Timeout`; finite timeouts report `NetworkFailure` without sending the actual request, while `Timeout.InfiniteTimeSpan` remains bounded by explicit cancellation. Browser-safelisted response headers do not need an expose grant, malformed CORS metadata fails closed, and response origin/list values trim only HTTP optional whitespace (`SP` and `HTAB`). It does not own application startup, contact an origin, use browser automation, or prove authentication, authorization, CSRF protection, or other server-side security properties.

Scenario targets must be relative application paths; raw C0 controls (`U+0000`–`U+001F`) and `DEL` (`U+007F`), URI authorities and schemes at the reference start, backslashes, UNC/device paths, and every slash-prefixed root-relative Windows drive-shaped path such as `/C:`, `/C:/orders`, or `/C:orders` are rejected before I/O. Percent-encoded path data remains valid, and raw scheme-like text in query or fragment data such as `?next=https://external.example/orders` or `?next=/C:orders` remains valid. Browser-forbidden `CONNECT`, `TRACE`, and `TRACK` methods, browser-managed headers, and conditional override headers (`X-HTTP-Method`, `X-HTTP-Method-Override`, and `X-Method-Override`) are also rejected before I/O. `Set-Cookie` and `Set-Cookie2` cannot be asserted as exposed response headers because browsers forbid exposing them.

See the repository [CORS contract guide](https://github.com/KeelMatrix/CorsSpec/blob/main/docs/cors-contracts.md) for the complete usage and caller-owned privacy boundary.

This package targets `.NET 8` (`net8.0`). Verified CI coverage for this release line is the GitHub-hosted Windows Server 2025 x64 runner (`windows-2025`), Linux x64, and macOS with a compatible ASP.NET Core test host; this does not claim a specific Windows 10/11 consumer host. See the repository's [platform support matrix](https://github.com/KeelMatrix/CorsSpec/blob/main/docs/platform-support.md).

CorsSpec's core verification has no independent network activity and remains caller-owned; with a local test host it is fully offline. It sends scenario metadata only through the caller-supplied `HttpClient`. After a response-level verdict, the optional `KeelMatrix.Telemetry` dependency requests one aggregate activation signal per execution (one for a matrix, not per cell). The activation payload is exactly `event`, `tool`, `tool_version`, `telemetry_version`, `schema_version`, pseudonymous `project_hash` and `installation_hash`, `runtime`, `os`, `ci`, and `timestamp`. It never contains origins, hostnames, endpoint paths or identity, endpoint-tied methods, request/response headers, cookies, credentials or tokens, bodies, or diagnostics. Invalid contracts and no-response executions do not request activation; telemetry failure never changes the result. Set `KEELMATRIX_NO_TELEMETRY=1` to suppress it; local and CI validation already do so. See the [repository privacy policy](https://github.com/KeelMatrix/CorsSpec/blob/main/PRIVACY.md) and the [maintained KeelMatrix.Telemetry privacy contract](https://github.com/KeelMatrix/Telemetry/blob/main/PRIVACY.md) for storage, queue, network, opt-out, and 90-day retention details.
