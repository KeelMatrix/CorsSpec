# Changelog

This file records consumer-facing changes to KeelMatrix.CorsSpec.

## [Unreleased]

### Added

- Upcoming changes are recorded here before release.

## [0.1.0] - 2026-09-24

### Added

- Provides executable CORS contracts for supplied `HttpClient` instances, including automatic simple-request and preflight execution.
- Reports header-level verdicts for origins, methods, requested headers, credentials, `Vary: Origin`, exposed headers, and preflight max-age without treating HTTP status as the CORS verdict.
- Supports bounded matrix verification for repeated origins, methods, and headers.
