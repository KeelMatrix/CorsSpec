# Changelog

This file records consumer-facing changes to KeelMatrix.CorsSpec.

## [Unreleased]

### Fixed

- Declares package classification for automatic catalog discovery, including a repository manifest fallback for existing published versions.
- Rejects additional packable C#, Visual Basic and F# projects during metadata validation, including projects outside the solution and inherited Release overrides.

## [0.1.0] - 2026-11-08

### Added

- Provides executable allowed and denied CORS contracts for .NET 8 applications through caller-owned clients created with `CorsTransport.CreateClient`, supporting direct ASP.NET Core TestHost handlers and configured BCL network handlers.
- Sends simple requests and browser-style preflights, checking origins, methods, requested headers, credentials, `Vary: Origin`, exposed headers, and optional preflight max-age; allowed preflights require a successful 2xx response before the actual request.
- Supports bounded contract matrices and structured diagnostics with assertions that identify the failed CORS dimension without exposing credentials or response bodies.
- Evaluates effective requests and immutable first-response evidence, rejecting unsupported clients, transports, redirects, and changed operations for both allowed and denied expectations. Clean preflight handler factories isolate caller defaults and credentials while honoring timeout and cancellation.
- Keeps core verification fully offline with a local test host and supplies no CORS contract data to optional shared activation telemetry.
