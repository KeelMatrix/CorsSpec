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

The package automatically constructs isolated browser-style preflights with `Accept: */*`, normalizes the six browser-standard request method names to uppercase, evaluates browser-relevant headers, and reports method, requested-header, credentials, origin, `Vary`, exposed-header, and max-age mismatches. Custom method casing is preserved and response allow-method tokens must match it exactly. A preflighted `GET`, `HEAD`, or `POST` does not require an allow-method token, but malformed present lists fail closed. The scenario API accepts declared names and can model values with `CorsRequestHeader`; one effective request representation drives classification, preflight names, and actual-request serialization. An explicit scenario value overrides a same-named `HttpClient.DefaultRequestHeaders` value, undeclared defaults fail closed, supported explicit values are normalized and serialized before I/O, and content headers are placed on request content. Browser safelist classification keeps safe `Accept`, language, `Content-Type`, and `Range` values simple when each normalized value is at most 128 UTF-8 bytes and the effective safelisted values total no more than 1024 UTF-8 bytes, while unsafe values and unknown names force a preflight. `Range` requires a single ASCII-digit range with a nonempty start and nondecreasing bounds; `Content-Type` uses MIME parsing with HTTP `SP`/`HTAB` boundaries and HTTP-token type/subtype rules. Handler-added values cannot be inspected before I/O, so use the clean factory for clients with default or handler-added headers. Factory-created preflights use the caller's `HttpClient.Timeout`; finite timeouts report `NetworkFailure` without sending the actual request, while `Timeout.InfiniteTimeSpan` remains bounded by explicit cancellation. Browser-safelisted response headers do not need an expose grant, and absent, valid-empty, wildcard, and explicit-member CORS lists follow their field-specific rules; malformed metadata fails closed. It does not own application startup, contact an origin, use browser automation, or prove authentication, authorization, CSRF protection, or other server-side security properties.

Scenario targets must be relative application paths; raw C0 controls (`U+0000`–`U+001F`) and `DEL` (`U+007F`), URI authorities and schemes at the reference start, backslashes, UNC/device paths, and every slash-prefixed root-relative Windows drive-shaped path such as `/C:`, `/C:/orders`, or `/C:orders` are rejected before I/O. Percent-encoded path data remains valid, and raw scheme-like text in query or fragment data such as `?next=https://external.example/orders` or `?next=/C:orders` remains valid. Browser-forbidden `CONNECT`, `TRACE`, and `TRACK` methods, browser-managed headers, and conditional override headers (`X-HTTP-Method`, `X-HTTP-Method-Override`, and `X-Method-Override`) are also rejected before I/O. `Set-Cookie` and `Set-Cookie2` cannot be asserted as exposed response headers because browsers forbid exposing them.

See the repository [CORS contract guide](https://github.com/KeelMatrix/CorsSpec/blob/main/docs/cors-contracts.md) for the complete usage and caller-owned privacy boundary.

This package targets `.NET 8` (`net8.0`). Verified CI coverage for this release line is the GitHub-hosted Windows Server 2025 x64 runner (`windows-2025`), Linux x64, and macOS with a compatible ASP.NET Core test host; this does not claim a specific Windows 10/11 consumer host. See the repository's [platform support matrix](https://github.com/KeelMatrix/CorsSpec/blob/main/docs/platform-support.md).

CorsSpec's core verification has no independent network activity and remains caller-owned; with a local test host it is fully offline. It sends scenario metadata only through the caller-supplied `HttpClient`. After an individual contract reaches a response-level verdict, CorsSpec calls the parameterless `TrackActivation()` method. Invalid contracts and operations without a response are ineligible, and no CORS contract data is supplied to telemetry. The shared package owns payload, deduplication, opt-out, identity/state, queueing, delivery, and failure handling. See the [repository privacy policy](https://github.com/KeelMatrix/CorsSpec/blob/main/PRIVACY.md) and the [maintained KeelMatrix.Telemetry privacy contract](https://github.com/KeelMatrix/Telemetry/blob/main/PRIVACY.md); local and CI validation suppress telemetry through the shared opt-out.
