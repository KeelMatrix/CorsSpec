# Development guide

## Prerequisites

Install the .NET 8 SDK selected by `global.json` and PowerShell 7.

## Local validation

From the repository root:

```powershell
pwsh ./scripts/Validate.ps1
```

The gate restores from `NuGet.config`, builds all solution projects in Release, runs unit and real-pipeline integration tests, creates the exact `.nupkg` and `.snupkg` artifacts under ignored `artifacts/packages`, binds both archives to the checked-out candidate SHA, inspects both archives, runs a clean package-reference consumer covering simple and preflight allowed/denied contracts, and audits dependencies. The package inspection reports a missing required `icon.png` while continuing the non-icon evidence; the release workflow adds `-RequireIcon` so publication remains fail-closed until that prerequisite is satisfied.

For an inner loop, run `dotnet test tests/KeelMatrix.CorsSpec.Tests -c Release` or `pwsh ./scripts/Validate.ps1 -Mode Focused -SkipPackage`.

The focused suites cover response grammar, effective header representation, handler preparation, supported encodings, 128/1024-byte boundaries, cookies/authorization and aggregate activation. Raw HTTP loopback tests assert bytes without an assertion parser. Real TestHost tests exercise application policies and assert host value arrays; raw/canonical formatter-sensitive fields must be preserved or explicitly unsupported before I/O. First-response tests cover self-redirects, returning chains, same/cross origins, preserving/changing statuses, both constructors and request phases, cancellation and disposal.

`FirstResponseProbes` is shared by the integration suite and the isolated built-package consumer. It checks in-place CORS header additions/removals/replacements, status and request-metadata rewriting, unchanged controls, both expectations and preflight paths, direct TestHost and real loopback transport, plus post-response cancellation/failure cleanup. Unit tests also ensure outer handlers cannot repair missing or incorrect original provenance.

`ClientBoundaryProbes` runs the shared-client workflow over direct TestServer and WebApplicationFactory handlers, both BCL network handlers, and clean/configured delegating pipelines. It checks ordinary test traffic, both constructors, GET/DELETE and allowed/denied expectations, clean preflights, retained actual-request defaults/credentials, caller/factory ownership, and actionable rejection before verification host I/O for already-created ordinary clients and opaque terminals. Factory controls show that redirect/cookie behavior and client settings do not transfer to the direct handler. ASP.NET testing packages are dependencies of tests and the consumer only; no host-observer adapter ships.

Normal validation runs `Test-IgnoreCoverage.ps1` with nested generated-file probes, rule-removal controls and product-file preservation. `Test-ReleaseStructure.ps1` exercises the reusable validator and normal/release failure propagation. Release notes use a bounded flat Markdown format: unindented ATX headings, optional closing hashes, and unindented hyphen bullets. Version headings/dates must be unique and valid; the first release has exactly one `Added` category with an entry. Comments, HTML, indentation, code fences, containers, setext headings and thematic breaks are rejected. Ordinary version/date edits remain mutable. This does not authorize release or require release-state finalization during development.

The gate also checks history, tracked text, package/symbol provenance and literal checkout paths containing spaces, brackets, ampersands and Unicode. The [contract guide](cors-contracts.md) maintains public behavior. No CLI/help ships.

## Platform evidence

The verified Windows Server 2025, Linux, and macOS support boundary and its required evidence are recorded in [docs/platform-support.md](platform-support.md). The Windows workflow is pinned to `windows-2025`; this is CI-environment evidence, not a claim for a specific Windows 10/11 consumer host. Each claimed operating system requires its own package-consumer and ASP.NET Core integration run.
