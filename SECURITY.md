# Security Policy

## Reporting a Vulnerability

Please report suspected vulnerabilities privately through the [GitHub Security tab](https://github.com/KeelMatrix/CorsSpec/security/advisories/new) and email `keelmatrix@gmail.com`. Do not disclose vulnerabilities in a public issue, pull request, or discussion until KeelMatrix has assessed the report.

Include the affected package version, a minimal reproduction, impact, and any relevant environment details. Do not include real credentials, cookies, authorization tokens, or private application data.

## Supported Versions

The latest stable package version receives security fixes. Older versions may receive fixes when the issue is severe and the change can be made without breaking supported consumers.

For ordinary defects or feature requests, use the public GitHub issue tracker after removing sensitive data.

CorsSpec evaluates the supported operation defined in [docs/cors-contracts.md](docs/cors-contracts.md), the maintained public contract. Clients must be created through `CorsTransport.CreateClient`. Preparation and the final send boundary observe effective headers, normalize HTTP whitespace, apply the selected encoding and safelist bounds, and reject missing values or changed operations. Network handlers must disable automatic redirects, authentication and decompression. Unsupported configurations cannot pass a denied expectation. Terminal identity alone does not establish redirect provenance. These checks do not replace application authentication, authorization, CSRF or network-security tests.

Activation is one single-contract verdict or one aggregate decision for a completed matrix with an eligible verdict. Invalid/unsupported configurations, no-response failures and matrices that propagate cancellation do not activate telemetry. The parameterless shared API receives no CORS or credential data. Opt-out, identity/state, deduplication, queueing, delivery and failure policy remain in `KeelMatrix.Telemetry`; see [PRIVACY.md](PRIVACY.md).
