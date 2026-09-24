# Privacy

KeelMatrix.CorsSpec does not collect or transmit telemetry. It does not contact an origin, resolve a hostname, inspect response bodies, or send request headers, cookies, bearer tokens, or authorization values anywhere. It only sends requests through the `HttpClient` supplied by the caller and evaluates the returned CORS headers locally.

The caller controls any network behavior of that `HttpClient`. Use a test host or another intentionally configured handler for offline validation.
