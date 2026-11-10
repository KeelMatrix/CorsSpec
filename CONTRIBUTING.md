# Contributing

## Before you begin

Install the .NET 8 SDK and restore from the repository's `NuGet.config`.

## Validation

Run `pwsh ./scripts/Validate.ps1` from the repository root. The script runs the Release build, tests, package inspection, dependency audit, and isolated package-consumer smoke test. The root `icon.png` is a required release asset; the package gate reports it as a prerequisite and never creates it.

Package metadata changes must keep `keelmatrix.website.json`, evaluated shipping project tags and canonical repository URLs aligned. C#, Visual Basic and F# test, sample and helper projects must remain non-packable under their effective Release settings, including inherited properties and targets overrides. The same gate checks source metadata and both package archives; see the [development guide](docs/DEV.md#package-discovery-metadata) for focused commands.

Keep public API changes documented in the shipping project's `PublicAPI.Shipped.txt` or `PublicAPI.Unshipped.txt` according to the release process. Do not add credentials, private application data, or generated build output. Local and CI validation must set `KEELMATRIX_NO_TELEMETRY=1`; activation-eligibility tests use only the injected call seam and must not create demand measurements. Shared payload, opt-out, and delivery behavior belongs to `KeelMatrix.Telemetry`; do not recreate those tests here. The CorsSpec adapter calls the parameterless shared API and must not pass contract data.

When changing request-generation or preflight-evaluation contracts, update `docs/cors-contracts.md` first and sweep the root/package README, privacy, security, platform, developer, and contributor documentation together. Transport changes must preserve `CorsTransport.CreateClient` preparation, immutable first-response CORS evidence and explicit unsupported outcomes. Evaluate status, headers and request provenance captured by value before caller handlers regain control; object identity alone does not prove response integrity. Matrix activation remains one aggregate eligibility decision. Preflight transport changes must preserve isolation from caller defaults and delegating-handler headers while keeping those headers on the actual request, and factory-created preflights must retain the caller's timeout and cancellation boundary; target validation must reject every slash-prefixed drive-shaped suffix at the reference start while preserving raw scheme-like and encoded query/fragment data. The unit and integration suites cover these boundaries, including a real ASP.NET Core middleware that rejects contaminated credentialed preflights.

Create the shared integration-test client through `CorsTransport.CreateClient` and keep application startup in the test setup. Preserve actionable `UnsupportedTransport` before verification host I/O for already-created ordinary clients; factory client settings and automatic redirect/cookie handlers do not transfer to direct handlers. No host-observer adapter ships. Run the shared client-boundary probes when changing this contract.

## Security

Follow [SECURITY.md](SECURITY.md) for vulnerability reports.
