# Changelog

This file records consumer-facing changes to KeelMatrix.CorsSpec.

## [Unreleased]

### Added

- Upcoming changes are recorded here before release.
- Requires successful (200–299) preflight responses before sending an allowed actual request, and reports a dedicated preflight-status failure.
- Enforces canonical browser origins, exact credential permission syntax, bounded immutable contract snapshots, browser-managed header rejection, and non-credentialed wildcard method/header exposure semantics.
- Verifies package and symbol archive identity, structure, provenance, and portable PDB SourceLink metadata; validates Windows, Linux, and macOS package-consumer evidence.

## [0.1.0] - 2026-09-24

### Added

- Provides executable CORS contracts for supplied `HttpClient` instances, including automatic simple-request and preflight execution.
- Reports browser-relevant verdicts for origins, methods, requested headers, credentials, `Vary: Origin`, exposed headers, and preflight max-age while requiring a successful status for allowed preflights.
- Supports bounded matrix verification for repeated origins, methods, and headers.
