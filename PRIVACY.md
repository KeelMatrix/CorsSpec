# Privacy

KeelMatrix.CorsSpec core verification is caller-owned and has no independent network activity. With `TestServer`, `WebApplicationFactory`, or another local handler it remains fully offline. CorsSpec never resolves or contacts the origin named in a scenario; the actual request uses the `HttpClient` supplied by the caller, while generated preflights can use a caller-supplied clean handler factory so default headers, cookies, authorization, and delegating-handler headers are not sent on `OPTIONS`. The optional activation boundary below is separate from core verification.

## Optional activation telemetry

After a contract execution receives an HTTP response and reaches a CORS verdict, CorsSpec requests one activation signal from `KeelMatrix.Telemetry`. This includes a failed CORS verdict: a valid response-level result is meaningful even when the contract fails. Invalid contracts and executions that fail before receiving a response do not request activation. A matrix execution requests one aggregate signal for the matrix, never one event per cell. CorsSpec does not request a recurring heartbeat. The shared client may deduplicate repeated activation requests for the same project identity.

The activation payload contains exactly these fields from the shared contract:

- `event` — `activation`.
- `tool` — the stable package/tool identifier.
- `tool_version` — the calling package version.
- `telemetry_version` — the shared telemetry package version.
- `schema_version` — the shared payload schema version.
- `project_hash` — a pseudonymous consuming-project identifier.
- `installation_hash` — a pseudonymous installation identifier.
- `runtime` — the runtime identifier, such as `net8.0`.
- `os` — the operating-system identifier.
- `ci` — whether the process is running in CI.
- `timestamp` — the UTC activation timestamp.

The maintained field-level source of truth is the [KeelMatrix.Telemetry privacy policy](https://github.com/KeelMatrix/Telemetry/blob/main/app/PRIVACY.md), with the activation implementation in [`ActivationEvent`](https://github.com/KeelMatrix/Telemetry/blob/main/app/src/KeelMatrix.Telemetry/Events/ActivationEvent.cs).

The activation payload never contains contract data: origin strings, hostnames, endpoint paths or other endpoint identity, methods tied to endpoint identity, request or response headers or values, cookies, credentials or bearer tokens, response bodies, or raw diagnostics. CorsSpec does not add credentials and does not copy response data into telemetry.

Delivery is asynchronous and best-effort. The shared dependency may use local per-user `telemetry.queue/pending`, `telemetry.queue/processing`, `telemetry.queue/dead`, `markers`, and `telemetry.salt` state. These files contain only the minimal queue, marker, and pseudonymous identity data needed for delivery and idempotency, not user content. Queue claims use a five-minute lease; lease expiry permits a duplicate-delivery window, so delivery is not exactly-once. The signal is sent over HTTPS to `https://telemetry.keelmatrix.com`, is size-limited, and network, construction, queue, or delivery failure cannot change a verification result or throw through CorsSpec.

Set `KEELMATRIX_NO_TELEMETRY=1` for local or CI validation and whenever telemetry is not wanted. KeelMatrix development and CI runs suppress telemetry and are not demand measurements. The shared dependency also honors its documented opt-out precedence; when telemetry is disabled, it sends no event, including queued backlog. Server-side telemetry retention is 90 days, after which data is automatically deleted. See the [shared privacy policy](https://github.com/KeelMatrix/Telemetry/blob/main/app/PRIVACY.md) for the complete opt-out, storage, and retention contract.

Browser-standard method names are normalized to their uppercase wire representation, while custom method casing is preserved. Generated preflights include only the browser-style `Accept: */*` header plus CORS preflight metadata; ordinary simple requests are not given that synthetic header. For a preflight caused by requested headers, `GET`, `HEAD`, and `POST` do not require an allow-method token, while custom allow-method tokens must preserve exact case and malformed lists fail closed. Response metadata accepts HTTP optional whitespace only (`SP` and `HTAB`); Unicode whitespace and invalid control/obs-text boundaries fail closed for origin, wildcard, list, and required `Vary: Origin` values. Raw C0 and `DEL` path controls, browser-managed, forbidden, and conditional override header names are rejected before the caller's handler is invoked. The scenario API accepts caller-added header names without their values; list those names for the actual request and use the clean preflight handler factory when defaults or handlers add them, so credentials and arbitrary custom headers never reach `OPTIONS`. Relative targets may contain raw scheme-like `://` text in query or fragment data; schemes and authorities are rejected only at the reference start. Use a test host or another intentionally configured handler for offline core validation.
