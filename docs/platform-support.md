# CorsSpec platform support

This document defines the verified platform boundary for the `KeelMatrix.CorsSpec` 0.1.0 package. The package targets `.NET 8` (`net8.0`) and uses the caller-supplied `HttpClient`; core verification has no OS-specific filesystem or independent network behavior. The optional shared activation telemetry is a separate, best-effort process boundary with the allowlisted activation fields and storage/network/retention contract in [PRIVACY.md](../PRIVACY.md) and the [maintained KeelMatrix.Telemetry privacy policy](https://github.com/KeelMatrix/Telemetry/blob/main/app/PRIVACY.md). It is opt-out and suppressed in validation runs; it does not change the supported runtime boundary.

## Verified support

| Platform | Runtime and host | Evidence | Support status |
| --- | --- | --- | --- |
| Windows x64 | Windows 10 x64, repository-selected .NET 8 SDK, ASP.NET Core 8 | The Windows CI leg runs the Release integration tests and isolated package-consumer smoke test against a real ASP.NET Core `TestServer`; the smoke test installs the built `.nupkg` and verifies simple and custom-method/requested-header preflight allowed/denied contracts. | Verified for this release line |
| Linux x64 | Ubuntu x64, repository-selected .NET 8 SDK, ASP.NET Core 8 | The Linux CI leg runs the same Release integration tests and isolated built-package consumer smoke test with an isolated NuGet cache and temporary directory, including simple and preflight contracts. | Verified for this release line |
| macOS | macOS hosted runner, repository-selected .NET 8 SDK, ASP.NET Core 8 | The macOS CI leg runs the same Release integration tests and isolated built-package consumer smoke test with an isolated NuGet cache and temporary directory, including simple and preflight contracts. | Verified for this release line |

The support claim depends on all three CI legs remaining green. The validation scripts use platform-native path joins, isolated temporary/cache paths, and the same package-consumer and ASP.NET Core evidence on each runner.

The request contract is platform-independent: browser-standard method names are normalized to uppercase, custom method casing is preserved for exact allow-method matching, generated preflights carry `Accept: */*`, preflighted `GET`/`HEAD`/`POST` requests do not require an allow-method token when present metadata is valid, and raw C0/`DEL` path controls are rejected before the caller's handler on every supported platform.

## Consumer boundary

The package is intended for `.NET 8` test projects and a caller-selected ASP.NET Core test host. CorsSpec does not start the host, contact the origin named in a scenario, or require browser automation. A consumer may supply an externally routed `HttpClient`, but that network behavior belongs to the consumer and is not platform evidence for CorsSpec itself. The only package-owned process boundary beyond core verification is the optional aggregate activation signal described in [PRIVACY.md](../PRIVACY.md); its failure cannot change a verification result.
