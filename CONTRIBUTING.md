# Contributing

## Before you begin

Install the .NET 8 SDK and restore from the repository's `NuGet.config`.

## Validation

Run `pwsh ./scripts/Validate.ps1` from the repository root. The script runs the Release build, tests, package inspection, dependency audit, and isolated package-consumer smoke test. The root `icon.png` is founder-owned; the package gate reports it as a required prerequisite and never creates it.

Keep public API changes documented in the shipping project's `PublicAPI.Shipped.txt` or `PublicAPI.Unshipped.txt` according to the release process. Do not add credentials, private application data, or generated build output.

## Security

Follow [SECURITY.md](SECURITY.md) for vulnerability reports.
