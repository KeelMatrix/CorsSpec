# CorsSpec platform support

This document defines the verified platform boundary for the `KeelMatrix.CorsSpec` 0.1.0 package. The package targets `.NET 8` (`net8.0`) and uses the caller-supplied `HttpClient`; it has no OS-specific filesystem or background-network behavior.

## Verified support

| Platform | Runtime and host | Evidence | Support status |
| --- | --- | --- | --- |
| Windows x64 | Windows 10 x64, .NET SDK 8.0.408, ASP.NET Core 8.0 | Release integration tests and the isolated package-consumer smoke test run against a real ASP.NET Core `TestServer`; the smoke test installs the built `.nupkg` and verifies one allowed and one denied contract. | Verified for this release line |

## Not yet verified

Linux and macOS are not part of the verified support claim for this release line. This repository does not currently provide package-consumer runs on those operating systems, so their behavior remains unverified even though the library has no intentional OS-specific implementation.

Do not infer Linux or macOS support from a Windows run. Extend this matrix only after the same built-package consumer and ASP.NET Core integration evidence has been run on the relevant operating system.

## Consumer boundary

The package is intended for `.NET 8` test projects and a caller-selected ASP.NET Core test host. CorsSpec does not start the host, contact the origin named in a scenario, or require browser automation. A consumer may supply an externally routed `HttpClient`, but that network behavior belongs to the consumer and is not platform evidence for CorsSpec itself.
