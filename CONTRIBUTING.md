# Contributing

## Before you begin

Install the .NET 8 SDK and restore from the repository's `NuGet.config`.

## Validation

Run `pwsh ./scripts/Validate.ps1` from the repository root. The script runs the Release build, tests, package inspection, dependency audit, and isolated package-consumer smoke test. The root `icon.png` is a required release asset; the package gate reports it as a prerequisite and never creates it.

Keep public API changes documented in the shipping project's `PublicAPI.Shipped.txt` or `PublicAPI.Unshipped.txt` according to the release process. Do not add credentials, private application data, or generated build output. Local and CI validation must set `KEELMATRIX_NO_TELEMETRY=1`; telemetry tests use synthetic sinks and must not create demand measurements. The payload-contract test must keep the shared activation allowlist and prohibited CORS-data categories aligned with `PRIVACY.md` and the maintained `KeelMatrix.Telemetry` privacy policy.

When changing request-generation or preflight-evaluation contracts, update `docs/cors-contracts.md` first and sweep the root/package README, privacy, security, platform, developer, and contributor documentation together. Preflight transport changes must preserve isolation from caller defaults and delegating-handler headers while keeping those headers on the actual request; target validation must distinguish schemes/authorities at the reference start from raw scheme-like query/fragment data. The unit and integration suites cover these boundaries, including a real ASP.NET Core middleware that rejects contaminated credentialed preflights.

## Security

Follow [SECURITY.md](SECURITY.md) for vulnerability reports.
