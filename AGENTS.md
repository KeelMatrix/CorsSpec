# Repository guide

## Navigation

- `src/KeelMatrix.CorsSpec` is the only packable project and contains the public contract model, request executor, header evaluator, and result model.
- `tests/KeelMatrix.CorsSpec.Tests` contains request-generation, header-interpretation, validation, and privacy tests.
- `tests/KeelMatrix.CorsSpec.IntegrationTests` runs the verifier against real ASP.NET Core `TestServer` middleware and endpoint policies.
- `tests/PackageSmoke` is a fresh non-packable consumer that references the built package through an isolated local feed.
- `scripts/Validate.ps1` is the local CI-equivalent gate. It also checks package contents and reports the required icon prerequisite.
- `docs/cors-contracts.md` contains the deeper consumer guide.

## Commands

```powershell
$env:KEELMATRIX_NO_TELEMETRY = '1'
pwsh ./scripts/Validate.ps1
dotnet test tests/KeelMatrix.CorsSpec.Tests -c Release
dotnet test tests/KeelMatrix.CorsSpec.IntegrationTests -c Release
```

## Invariants

- The shipping library has no ASP.NET Core, test-framework, or browser dependency. It has an optional, best-effort `KeelMatrix.Telemetry` dependency for one aggregate activation signal after a response-level contract verdict; core request verification remains caller-owned. The activation allowlist is `event`, `tool`, `tool_version`, `telemetry_version`, `schema_version`, pseudonymous project/installation hashes, `runtime`, `os`, `ci`, and `timestamp`; no CORS contract data is sent. See `PRIVACY.md` and the maintained shared privacy policy for queue, network, opt-out, and retention behavior.
- `Origin` is never a destination. Only the caller's `HttpClient` performs I/O.
- Allowed preflight verdicts require both a successful HTTP status and browser-relevant headers; actual-response status is never sufficient by itself.
- Generated preflights include exactly the package-owned `OPTIONS`, `Origin`, `Access-Control-Request-Method`, optional `Access-Control-Request-Headers`, and `Accept: */*` metadata. When caller defaults or delegating handlers add headers, use `CorsVerifier(HttpClient, Func<HttpMessageHandler>)` with a clean factory; the actual request retains the caller's headers. The one-argument constructor fails closed when `DefaultRequestHeaders` is non-empty.
- Factory-created preflights use the caller's `HttpClient.Timeout` and cancellation boundary; finite timeouts produce `NetworkFailure` without sending the actual request, and returned handlers are disposed after completion.
- For preflights caused by requested headers, valid `GET`/`HEAD`/`POST` methods do not require an `Access-Control-Allow-Methods` match; malformed present method lists fail closed, non-safelisted methods require an exact match, standard method names use normalized matching, and custom method tokens are case-sensitive.
- Scenario header names do not carry values; every requested caller-added header name conservatively forces a preflight, including `Accept`, `Accept-Language`, `Content-Language`, `Content-Type`, and `Range`.
- Browser-standard method names (`DELETE`, `GET`, `HEAD`, `OPTIONS`, `POST`, and `PUT`) are normalized to uppercase before simple-method classification and request construction; custom method casing is preserved. Browser-forbidden methods and conditional override header names are rejected because their browser semantics are not modeled.
- Scenario targets remain relative application paths; raw C0/`DEL` controls, URI authorities or schemes at the reference start, backslashes, UNC/device paths, and every slash-prefixed root-relative Windows drive-shaped suffix are rejected before I/O. Raw scheme-like `://` and drive-shaped text in query or fragment data remain valid.
- The platform matrix scopes Windows evidence to the pinned GitHub-hosted `windows-2025` Windows Server 2025 x64 runner and does not claim a specific Windows 10/11 consumer host.
- Response exposure honors the seven browser CORS-safelisted response headers, and malformed CORS list, credential, or max-age metadata never grants permission.
- `Set-Cookie` and `Set-Cookie2` are forbidden response-header expectations and fail before I/O.
- Diagnostics never copy response bodies, cookies, authorization values, bearer tokens, or arbitrary response headers.
- The root `icon.png` is a required release asset. Do not create, copy, modify, inspect, delete, commit, or push icon bytes.
- `scripts/Test-CommitHistory.ps1` checks every reachable commit for developer-facing authorship and message metadata; CI must fetch full history for that check.
- Tests and consumers are explicitly non-packable and are not included in the public package.

## Validation

Use the focused unit project during development. Before handoff, run `scripts/Validate.ps1`; it restores from `NuGet.config`, builds Release, runs both test projects, packs and inspects the exact artifact set, audits dependencies, and runs the package consumer. A missing icon is reported while the remaining checks continue; use `scripts/Validate.ps1 -RequireIcon` for the fail-closed release gate.
