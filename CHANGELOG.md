# Changelog

This file records consumer-facing changes to KeelMatrix.CorsSpec.

## [Unreleased]

### Added

- Upcoming changes are recorded here before release.
- Requires successful (200–299) preflight responses before sending an allowed actual request, and reports a dedicated preflight-status failure.
- Enforces canonical browser origins, exact credential permission syntax, bounded immutable contract snapshots, browser-managed header rejection, and non-credentialed wildcard method/header exposure semantics.
- Honors the browser CORS response-header safelist, rejects malformed response metadata instead of accepting a valid token from an invalid list, and treats all value-sensitive caller header names conservatively as preflighted because the scenario API does not model values.
- Rejects absolute target paths, slash-prefixed root-relative Windows drive paths, Windows separators, browser-forbidden methods and conditional override headers, and impossible `Set-Cookie`/`Set-Cookie2` exposure expectations before I/O.
- Normalizes the browser-standard method names `DELETE`, `GET`, `HEAD`, `OPTIONS`, `POST`, and `PUT` to uppercase while preserving custom method casing.
- Rejects raw C0 and `DEL` target-path controls before caller I/O while allowing intentionally percent-encoded path data.
- Verifies package and symbol archive identity, structure, provenance, and portable PDB SourceLink metadata against the checked-out candidate SHA; the package-consumer smoke covers simple and custom preflight behavior on Windows, Linux, and macOS.
- Builds browser-style preflights with `Accept: */*`, preserves exact casing for custom method allow-list matching, and permits preflighted `GET`, `HEAD`, and `POST` scenarios without requiring an allow-method token while still rejecting malformed lists.

## [0.1.0] - 2026-09-24

### Added

- Provides executable CORS contracts for supplied `HttpClient` instances, including automatic simple-request and preflight execution.
- Reports browser-relevant verdicts for origins, methods, requested headers, credentials, `Vary: Origin`, exposed headers, and preflight max-age while requiring a successful status for allowed preflights.
- Supports bounded matrix verification for repeated origins, methods, and headers.
