# Contributing

## Before you begin

Install the .NET 8 SDK and restore from the repository's `NuGet.config`.

## Validation

Run `pwsh ./scripts/Validate.ps1` from the repository root. The script runs the Release build, tests, package inspection, dependency audit, and isolated package-consumer smoke test. The root `icon.png` is a required release asset; the package gate reports it as a prerequisite and never creates it.

Keep public API changes documented in the shipping project's `PublicAPI.Shipped.txt` or `PublicAPI.Unshipped.txt` according to the release process. Do not add credentials, private application data, or generated build output.

When changing request-generation contracts, update `docs/cors-contracts.md` first and sweep the root/package README, privacy, security, platform, developer, and contributor documentation together. The unit suite covers browser-standard method normalization, custom-method casing, and raw path-control boundaries.

## Security

Follow [SECURITY.md](SECURITY.md) for vulnerability reports.
