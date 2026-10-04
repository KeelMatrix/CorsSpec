# Contributing

## Before you begin

Install the .NET 8 SDK and restore from the repository's `NuGet.config`.

## Validation

Run `pwsh ./scripts/Validate.ps1` from the repository root. The script runs the Release build, tests, package inspection, dependency audit, and isolated package-consumer smoke test. The root `icon.png` is a required release asset; the package gate reports it as a prerequisite and never creates it.

Keep public API changes documented in the shipping project's `PublicAPI.Shipped.txt` or `PublicAPI.Unshipped.txt` according to the release process. Do not add credentials, private application data, or generated build output. Local and CI validation must set `KEELMATRIX_NO_TELEMETRY=1`; activation-eligibility tests use only the injected call seam and must not create demand measurements. Shared payload, opt-out, and delivery behavior belongs to `KeelMatrix.Telemetry`; do not recreate those tests here. The CorsSpec adapter calls the parameterless shared API and must not pass contract data.

When changing request-generation or preflight-evaluation contracts, update `docs/cors-contracts.md` first and sweep the root/package README, privacy, security, platform, developer, and contributor documentation together. Preflight transport changes must preserve isolation from caller defaults and delegating-handler headers while keeping those headers on the actual request, and factory-created preflights must retain the caller's timeout and cancellation boundary; target validation must reject every slash-prefixed drive-shaped suffix at the reference start while preserving raw scheme-like and encoded query/fragment data. The unit and integration suites cover these boundaries, including a real ASP.NET Core middleware that rejects contaminated credentialed preflights.

## Security

Follow [SECURITY.md](SECURITY.md) for vulnerability reports.
