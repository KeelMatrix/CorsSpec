# Changelog

This file records consumer-facing changes to KeelMatrix.CorsSpec.

## [Unreleased]

### Added

- Upcoming changes are recorded here before release.
- Factory-created preflights now inherit the caller's `HttpClient.Timeout` and cancellation boundary; finite timeout failures are reported as `NetworkFailure`, do not send the actual request, and still dispose returned handlers.
- Provides a dedicated preflight handler factory so generated `OPTIONS` requests contain only browser preflight metadata while actual requests retain caller defaults and handler behavior; clients with default request headers fail closed unless the dedicated factory is supplied.
- Accepts raw scheme-like `://` text in relative target query and fragment data while continuing to reject schemes and authorities at the start of a target.
- Requires successful (200–299) preflight responses before sending an allowed actual request, and reports a dedicated preflight-status failure.
- Rejects redirect responses and final responses whose observed request URI differs from the requested operation with `RedirectNotSupported` instead of evaluating lost redirect provenance.
- Enforces canonical browser origins, exact credential permission syntax, bounded immutable contract snapshots, browser-managed header rejection, and non-credentialed wildcard method/header exposure semantics.
- Honors the browser CORS response-header safelist, rejects malformed response metadata instead of accepting a valid token from an invalid list, and classifies modeled caller header values using the browser request-header safelist.
- Rejects absolute target paths, slash-prefixed root-relative Windows drive paths, Windows separators, browser-forbidden methods and conditional override headers, and impossible `Set-Cookie`/`Set-Cookie2` exposure expectations before I/O.
- Rejects every slash-prefixed root-relative Windows drive-shaped target, including ordinary, nested, query-bearing, and fragment-bearing suffixes, while preserving scheme-like and encoded data after query/fragment delimiters.
- Normalizes the browser-standard method names `DELETE`, `GET`, `HEAD`, `OPTIONS`, `POST`, and `PUT` to uppercase while preserving custom method casing.
- Rejects raw C0 and `DEL` target-path controls before caller I/O while allowing intentionally percent-encoded path data.
- Verifies package and symbol archive identity, structure, provenance, and portable PDB SourceLink metadata against the checked-out candidate SHA; the package-consumer smoke covers simple and custom preflight behavior on Windows, Linux, and macOS.
- Builds browser-style preflights with `Accept: */*`, applies exact response-token casing for allow-method matching, and permits preflighted `GET`, `HEAD`, and `POST` scenarios without requiring an allow-method token while still rejecting malformed lists.
- Accepts only HTTP optional whitespace (`SP` and `HTAB`) around CORS response metadata and fails closed on Unicode whitespace, invalid controls, obs-text boundaries, and malformed `Vary: Origin` lists.
- Provides one optional, best-effort aggregate activation signal after a response-level contract verdict, with matrix executions aggregated across cells and no recurring heartbeat. The shared activation payload is allowlisted to `event`, `tool`, `tool_version`, `telemetry_version`, `schema_version`, pseudonymous project/installation hashes, `runtime`, `os`, `ci`, and `timestamp`; origins, endpoint identity, endpoint-tied methods, request/response headers, cookies, credentials, bodies, and diagnostics are excluded. `KEELMATRIX_NO_TELEMETRY=1` suppresses it.
- Pins Windows validation to the GitHub-hosted Windows Server 2025 x64 `windows-2025` image and scopes the platform documentation to that CI evidence rather than a Windows 10/11 consumer-host claim.
- Documents the shared telemetry queue, HTTPS endpoint, opt-out boundary, 90-day retention, and direct `KeelMatrix.Telemetry` privacy source of truth; core verification remains caller-owned and can be fully offline with a local test host.

## [0.1.0] - 2026-09-24

### Added

- Provides executable CORS contracts for supplied `HttpClient` instances, including automatic simple-request and preflight execution.
- Reports browser-relevant verdicts for origins, methods, requested headers, credentials, `Vary: Origin`, exposed headers, and preflight max-age while requiring a successful status for allowed preflights.
- Supports bounded matrix verification for repeated origins, methods, and headers.
