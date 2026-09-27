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

The focused unit suite covers every casing variant of the six browser-standard methods, custom-method exact-case allow-list matching, safelisted-method preflight behavior including credential and wildcard boundaries, exact `Accept: */*` preflight construction, absolute/authority/separator/UNC/device and slash-prefixed root-relative Windows drive path rejection, all raw C0/`DEL` path controls, and valid percent-encoded path data. The validation gate also scans all reachable commit history and requires CI to fetch full history.

## Platform evidence

The verified Windows, Linux, and macOS support boundary and its required evidence are recorded in [docs/platform-support.md](platform-support.md). Each claimed operating system requires its own package-consumer and ASP.NET Core integration run.
