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

The focused unit suite covers every casing variant of the six browser-standard methods, custom-method exact-case allow-list matching, safelisted-method preflight behavior including credential and wildcard boundaries, exact isolated `OPTIONS` metadata with default and delegating-handler header cases, factory-preflight timeout/cancellation/fault and disposal bounds across matrix positions, absolute/authority/separator/UNC/device and every slash-prefixed root-relative Windows drive-shaped suffix rejection, raw scheme-like `://` query/fragment boundary cases, all raw C0/`DEL` path controls, valid percent-encoded path data, one activation request per meaningful execution, matrix aggregation, suppression, telemetry failure isolation, and the captured shared activation-payload allowlist. The validation gate also scans all reachable commit history and requires CI to fetch full history.

## Platform evidence

The verified Windows Server 2025, Linux, and macOS support boundary and its required evidence are recorded in [docs/platform-support.md](platform-support.md). The Windows workflow is pinned to `windows-2025`; this is CI-environment evidence, not a claim for a specific Windows 10/11 consumer host. Each claimed operating system requires its own package-consumer and ASP.NET Core integration run.
