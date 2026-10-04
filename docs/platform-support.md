# CorsSpec platform support

This document defines the verified platform boundary for the `KeelMatrix.CorsSpec` 0.1.0 package. The package targets `.NET 8` (`net8.0`) and uses the caller-supplied `HttpClient`; core verification has no OS-specific filesystem or independent network behavior. A response-level CORS verdict may call the parameterless shared activation API without passing contract data. The shared package owns telemetry payload, opt-out, state, delivery, failure, and retention behavior; see [PRIVACY.md](../PRIVACY.md) and the [maintained KeelMatrix.Telemetry privacy policy](https://github.com/KeelMatrix/Telemetry/blob/main/PRIVACY.md). Validation suppresses through the shared process opt-out; this does not change the supported runtime boundary.

## Verified support

| Platform | Runtime and host | Evidence | Support status |
| --- | --- | --- | --- |
| Windows Server 2025 x64 runner | GitHub-hosted Windows Server 2025 x64 runner (`windows-2025`), repository-selected .NET 8 SDK, ASP.NET Core 8 | The pinned `windows-2025` CI leg runs the Release integration tests and isolated package-consumer smoke test against a real ASP.NET Core `TestServer`; the smoke test installs the built `.nupkg` and verifies simple and custom-method/requested-header preflight allowed/denied contracts. | Verified CI environment for this release line; not a claim for Windows 10/11 consumer hosts |
| Linux x64 | Ubuntu x64, repository-selected .NET 8 SDK, ASP.NET Core 8 | The Linux CI leg runs the same Release integration tests and isolated built-package consumer smoke test with an isolated NuGet cache and temporary directory, including simple and preflight contracts. | Verified for this release line |
| macOS | macOS hosted runner, repository-selected .NET 8 SDK, ASP.NET Core 8 | The macOS CI leg runs the same Release integration tests and isolated built-package consumer smoke test with an isolated NuGet cache and temporary directory, including simple and preflight contracts. | Verified for this release line |

The support claim depends on all three CI legs remaining green. The Windows claim is intentionally scoped to the pinned GitHub-hosted Windows Server 2025 x64 runner; this evidence does not establish compatibility with a specific Windows 10 or Windows 11 consumer host. The validation scripts use platform-native path joins, isolated temporary/cache paths, and the same package-consumer and ASP.NET Core evidence on each runner.

The request contract is platform-independent: browser-standard method names are normalized to uppercase, response allow-method tokens use exact casing, generated preflights carry only the package-owned `Accept: */*` and CORS metadata when a clean handler factory is supplied, factory-created preflights inherit the caller's timeout and cancellation boundary, preflighted `GET`/`HEAD`/`POST` requests do not require an allow-method token when present metadata is valid, raw C0/`DEL` path controls are rejected before the caller's handler, and raw scheme-like `://` text remains valid in relative query or fragment data on every supported platform.

## Consumer boundary

The package is intended for `.NET 8` test projects and a caller-selected ASP.NET Core test host. CorsSpec does not start the host, contact the origin named in a scenario, or require browser automation. A consumer may supply an externally routed `HttpClient` for actual requests and a clean `HttpMessageHandler` factory for preflights; factory preflight timeouts and cancellation follow that caller client, while the network behavior itself belongs to the consumer and is not platform evidence for CorsSpec. The shared activation call described in [PRIVACY.md](../PRIVACY.md) does not change the supported runtime boundary.
