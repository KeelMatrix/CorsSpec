# Security Policy

## Reporting a Vulnerability

Please report suspected vulnerabilities privately through the [GitHub Security tab](https://github.com/KeelMatrix/CorsSpec/security/advisories/new) and email `keelmatrix@gmail.com`. Do not disclose vulnerabilities in a public issue, pull request, or discussion until KeelMatrix has assessed the report.

Include the affected package version, a minimal reproduction, impact, and any relevant environment details. Do not include real credentials, cookies, authorization tokens, or private application data.

## Supported Versions

The latest stable package version receives security fixes. Older versions may receive fixes when the issue is severe and the change can be made without breaking supported consumers.

For ordinary defects or feature requests, use the public GitHub issue tracker after removing sensitive data.

CorsSpec validates browser-facing method, request-header, and target-path representations before invoking the caller's handler. One effective request representation drives request-header validation, classification, and serialization: an explicit scenario value overrides a same-named client default, supported explicit values are normalized before I/O, and invalid or unsupported values fail closed. Value-sensitive request-header classification applies the 128-byte per-value and 1024-byte aggregate safelist limits to the supported UTF-8 representation, parses `Range` as a single ASCII-digit nondecreasing range, and parses `Content-Type` with HTTP whitespace and token rules rather than Unicode `Trim` semantics. It normalizes the six browser-standard method names, preserves custom method casing for exact allow-method evaluation, constructs isolated preflights with `Accept: */*` through a clean handler factory, carries the caller's timeout and cancellation boundary into factory preflights, and rejects raw control characters, URI authorities/schemes at the reference start, separators, and every slash-prefixed root-relative Windows drive-shaped target in relative paths, regardless of its suffix. Raw scheme-like `://` text and drive-shaped text in query or fragment data remain valid. These checks do not replace authentication, authorization, CSRF, or network-security controls in the application under test.

The optional activation call is eligible only after an individual contract reaches a response-level verdict. CorsSpec calls the parameterless shared API and does not pass origins, endpoint paths, methods, request/response headers, cookies, credentials, response bodies, or diagnostics. The shared `KeelMatrix.Telemetry` package owns event fields, opt-out, state, queueing, delivery, and failure behavior; see [PRIVACY.md](PRIVACY.md) and its linked shared privacy contract.
