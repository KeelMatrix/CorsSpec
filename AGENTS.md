# Repository guide

## Navigation

- `src/KeelMatrix.CorsSpec` is the only packable project and contains the public contract model, request executor, header evaluator, and result model.
- `tests/KeelMatrix.CorsSpec.Tests` contains request-generation, header-interpretation, validation, and privacy tests.
- `tests/KeelMatrix.CorsSpec.IntegrationTests` runs the verifier against real ASP.NET Core `TestServer` middleware and endpoint policies.
- `tests/PackageSmoke` is a fresh non-packable consumer that references the built package through an isolated local feed.
- `scripts/Validate.ps1` is the local CI-equivalent gate. It also checks package contents and reports the required icon prerequisite.
- `scripts/Validate-WebsiteMetadata.ps1` checks the root classification manifest against evaluated Release source metadata for C#/VB/F# projects and optional built package/symbol archives; `Test-WebsiteMetadata.ps1` covers metadata rejection and stage failure propagation, including `Test-WebsiteProjectSelection.ps1` for project selection and overrides.
- `docs/cors-contracts.md` contains the deeper consumer guide.

## Commands

```powershell
$env:KEELMATRIX_NO_TELEMETRY = '1'
pwsh ./scripts/Validate.ps1
dotnet test tests/KeelMatrix.CorsSpec.Tests -c Release
dotnet test tests/KeelMatrix.CorsSpec.IntegrationTests -c Release
```

## Invariants

- `docs/cors-contracts.md` is the maintained public contract. Keep API XML, examples, packed README and tests aligned with it.
- The shipping library has no ASP.NET Core, test-framework or browser dependency. Activation is one single-contract verdict or one aggregate eligibility call per completed matrix. Shared telemetry policy stays in `KeelMatrix.Telemetry`.
- Clients come from `CorsTransport.CreateClient`. Preparation captures effective headers without terminal I/O; execution checks the same method, destination, origin, values and encoding. Missing information and unsupported terminals cannot earn either verdict polarity.
- The same caller-owned client serves ordinary integration tests and verification. Already-created ordinary TestServer, WebApplicationFactory and BCL clients fail before verification host I/O; factory client settings and automatic redirect/cookie handlers do not transfer to direct handlers. No host-observer adapter is supplied.
- Network handlers must disable automatic redirects, authentication and decompression. Direct TestHost handlers and exact BCL terminals are supported. Terminal identity is only a consistency check, not redirect provenance.
- Capture first-response status, CORS metadata and request method/URI by value at the terminal boundary. Evaluate that snapshot; caller handlers cannot rewrite verdict evidence through in-place mutation. Response identity remains a separate replacement/retry check.
- Clean preflight factories keep caller defaults and credentials off OPTIONS, inherit timeout/cancellation, and dispose owned responses and handlers on failure as well as success.
- Raw wire tests cover normalization, formatting, encodings and 128/1024-byte boundaries. Cookies are browser credentials; Authorization is an author header.
- Browser-standard method names (`DELETE`, `GET`, `HEAD`, `OPTIONS`, `POST`, and `PUT`) are normalized to uppercase before simple-method classification and request construction; custom method casing is preserved. Browser-forbidden methods and conditional override header names are rejected because their browser semantics are not modeled.
- Scenario targets remain relative application paths; raw C0/`DEL` controls, URI authorities or schemes at the reference start, backslashes, UNC/device paths, and every slash-prefixed root-relative Windows drive-shaped suffix are rejected before I/O. Raw scheme-like `://` and drive-shaped text in query or fragment data remain valid.
- The platform matrix scopes Windows evidence to the pinned GitHub-hosted `windows-2025` Windows Server 2025 x64 runner and does not claim a specific Windows 10/11 consumer host.
- Response exposure honors the seven browser CORS-safelisted response headers, and malformed CORS list, credential, or max-age metadata never grants permission.
- `Set-Cookie` and `Set-Cookie2` are forbidden response-header expectations and fail before I/O.
- Diagnostics never copy response bodies, cookies, authorization values, bearer tokens, or arbitrary response headers.
- The root `icon.png` is a required release asset. Do not create, copy, modify, delete, commit, or push icon bytes.
- `scripts/Test-CommitHistory.ps1` checks every reachable commit for developer-facing authorship and message metadata; CI must fetch full history for that check.
- Tests, consumers, samples and helpers are explicitly non-packable under effective Release settings, regardless of C#/VB/F# language or solution membership, and are not included in the public package.

## Validation

Use the focused unit project during development. Before handoff, run `scripts/Validate.ps1`; it restores from `NuGet.config`, builds Release, runs both test projects, packs and inspects the exact artifact set, audits dependencies, and runs the package consumer. A missing icon is reported while the remaining checks continue; use `scripts/Validate.ps1 -RequireIcon` for the fail-closed release gate.
