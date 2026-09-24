# Repository guide

## Navigation

- `src/KeelMatrix.CorsSpec` is the only packable project and contains the public contract model, request executor, header evaluator, and result model.
- `tests/KeelMatrix.CorsSpec.Tests` contains request-generation, header-interpretation, validation, and privacy tests.
- `tests/KeelMatrix.CorsSpec.IntegrationTests` runs the verifier against real ASP.NET Core `TestServer` middleware and endpoint policies.
- `tests/PackageSmoke` is a fresh non-packable consumer that references the built package through an isolated local feed.
- `scripts/Validate.ps1` is the local CI-equivalent gate. It also checks package contents and reports the founder-owned icon prerequisite.
- `docs/cors-contracts.md` contains the deeper consumer guide.

## Commands

```powershell
$env:KEELMATRIX_NO_TELEMETRY = '1'
pwsh ./scripts/Validate.ps1
dotnet test tests/KeelMatrix.CorsSpec.Tests -c Release
dotnet test tests/KeelMatrix.CorsSpec.IntegrationTests -c Release
```

## Invariants

- The shipping library has no ASP.NET Core, test-framework, browser, or telemetry dependency.
- `Origin` is never a destination. Only the caller's `HttpClient` performs I/O.
- CORS verdicts are based on browser-relevant headers, never status alone.
- Diagnostics never copy response bodies, cookies, authorization values, bearer tokens, or arbitrary response headers.
- The root `icon.png` is founder-owned. Do not create, copy, modify, inspect, delete, commit, or push icon bytes.
- Tests and consumers are explicitly non-packable and are not included in the public package.

## Validation

Use the focused unit project during development. Before handoff, run `scripts/Validate.ps1`; it restores from `NuGet.config`, builds Release, runs both test projects, packs and inspects the exact artifact set, audits dependencies, and runs the package consumer. A missing icon is reported as a release-gate failure while the remaining checks continue.
