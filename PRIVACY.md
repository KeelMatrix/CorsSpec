# Privacy

KeelMatrix.CorsSpec does not collect or transmit telemetry and performs no independent or background network activity. It does not contact the origin named in a scenario or resolve a hostname. It intentionally sends the scenario path, `Origin`, and preflight metadata through the `HttpClient` supplied by the caller, then evaluates returned CORS headers locally.

The caller controls any network behavior of that `HttpClient`, including routing and any caller-added headers. CorsSpec does not add cookies, bearer tokens, or authorization values, and it does not copy response bodies, credentials, or diagnostics to a CorsSpec service. Use a test host or another intentionally configured handler for offline validation.
