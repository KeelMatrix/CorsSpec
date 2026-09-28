# CorsSpec contracts

`KeelMatrix.CorsSpec` verifies observable CORS behavior through a caller-supplied `HttpClient`. It does not start an application or infer policy from ASP.NET Core internals.

## Simple and preflight requests

Before classification and request construction, browser-standard request methods `DELETE`, `GET`, `HEAD`, `OPTIONS`, `POST`, and `PUT` are normalized to uppercase. `GET`, `HEAD`, and `POST` scenarios with no unsafe requested header value are executed as one actual request. Other methods or unsafe/unknown requested headers first receive an `OPTIONS` request with:

- `Origin` set to the scenario origin;
- `Accept: */*` to match browser preflight construction;
- `Access-Control-Request-Method` set to the normalized browser-standard method or the exact-cased custom method;
- `Access-Control-Request-Headers` set to the normalized requested header names.

For an allowed preflight, CorsSpec requires a 2xx response and checks the allow-origin, allow-methods, allow-headers, credentials, max-age when asserted, and required `Vary: Origin` behavior, then sends the actual request. A non-2xx response fails an allowed preflight even when its CORS headers are otherwise permissive. When a preflight is caused by requested headers, the safelisted methods `GET`, `HEAD`, and `POST` do not require a matching `Access-Control-Allow-Methods` token, but a present malformed method list still fails closed. Other methods require an exact allow-method match, including exact response-token casing; custom method tokens are also case-sensitive. For a non-credentialed contract, a valid method wildcard is accepted. A denied contract passes immediately when the preflight blocks access, but when preflight grants access CorsSpec executes and evaluates the actual response too.

Other valid method names preserve the caller's casing because custom methods remain case-sensitive. Browser-forbidden `CONNECT`, `TRACE`, and `TRACK` methods are rejected before a request is created.

### Preflight transport isolation

The actual request always uses the caller-supplied `HttpClient`, so its configured defaults, cookies, authorization, and delegating-handler behavior remain part of the actual-request test. A generated preflight must not inherit those caller-wide headers. When the client has default headers or its handler adds headers, provide a clean handler factory for the preflight path:

```csharp
var verifier = new CorsVerifier(client, () => testServer.CreateHandler());
var result = await verifier.VerifyAsync(contract);
```

The factory is called for each generated preflight, and CorsSpec disposes the returned handler after that request. Factory preflights use a temporary client over the returned handler with the caller's `HttpClient.Timeout`, so finite timeouts bound preflight completion; `Timeout.InfiniteTimeSpan` remains caller-bounded by the supplied cancellation token. A timeout is reported as `NetworkFailure` and prevents the actual request; caller cancellation remains cancellable and propagates. The factory must not add caller credentials or arbitrary headers. The one-argument constructor rejects a preflight when `HttpClient.DefaultRequestHeaders` is non-empty; with no defaults it retains the existing caller-client path for compatibility. Use the factory whenever a delegating handler adds headers so only `OPTIONS`, `Origin`, `Access-Control-Request-Method`, optional `Access-Control-Request-Headers`, and `Accept: */*` reach the preflight pipeline.

The scenario API accepts declared names for caller-added headers and also supports `CorsRequestHeader` values for the actual request. Value-sensitive safelisting is classified for `Accept`, language headers, `Content-Type`, and `Range`; a safe value is at most 128 bytes, and all effective safelisted values across the declared names must total no more than 1024 bytes. A safe `GET` with `Accept` remains a simple request, while an unsafe value or an unknown header forces a preflight. `Range` is safelisted only for one ASCII-digit range with a nonempty start and start no greater than end. `Content-Type` uses the MIME parser: only HTTP `SP`/`HTAB` boundaries are ignored, and its type/subtype must be HTTP tokens. Declared `HttpClient.DefaultRequestHeaders` values are part of that effective classification, so max-age validation uses the same preflight predicate as request execution. Explicit `HttpClient.DefaultRequestHeaders` names must be declared by the scenario; handler-added values cannot be inspected before I/O and remain the caller's responsibility to keep consistent with the declaration. Use the dedicated preflight handler factory above to keep declared actual-request values off `OPTIONS`. Browser-managed, forbidden, and CORS protocol header names are rejected before I/O, including the conditional override family `X-HTTP-Method`, `X-HTTP-Method-Override`, and `X-Method-Override`.

Scenario targets must remain relative application paths. Raw C0 control characters (`U+0000` through `U+001F`) and `DEL` (`U+007F`) are rejected before a caller handler can receive a request; percent-encoded path data remains valid. URI authorities and schemes at the start of the reference, any backslash separator, UNC/device paths, and every slash-prefixed root-relative Windows drive-shaped path—including `/C:`, `/C:/orders`, `/C:orders`, nested segments, and suffixes containing query or fragment delimiters—are also rejected before I/O. Valid `/orders`, `orders`, and query-bearing relative targets remain relative, including raw scheme-like text such as `?next=https://external.example/orders`, `?next=/C:foo`, or `#next=https://external.example/orders`; only a scheme or authority at the start is rejected. Expected exposed response headers cannot include the forbidden `Set-Cookie` or `Set-Cookie2` names, including casing variants or duplicates; that impossible expectation fails before I/O.

For response exposure, the browser CORS safelist is honored for `Cache-Control`, `Content-Language`, `Content-Length`, `Content-Type`, `Expires`, `Last-Modified`, and `Pragma`; these assertions do not require `Access-Control-Expose-Headers`. Other asserted response headers require a complete, valid expose-header list. Allow-method, allow-header, expose-header, and required `Vary` lists tolerate empty members but reject non-token bytes and non-HTTP whitespace. `Access-Control-Allow-Headers` may combine `*` with explicit members; wildcard-only metadata does not cover `Authorization`, while an explicit `Authorization` member does. Origin and wildcard values trim only HTTP optional whitespace (`SP` and `HTAB`); Unicode whitespace, controls, and obs-text at the boundary remain malformed. For credentialed scenarios, `Access-Control-Allow-Credentials` accepts one lowercase `true` value with HTTP optional whitespace around it; asserted `Access-Control-Max-Age` values are one ASCII digit-only value with optional HTTP whitespace around it. Malformed metadata never grants the permission that field is meant to establish.

Redirects are outside the supported transport contract. A 301, 302, 303, 307, or 308 response, or a response whose observed request URI differs from the requested URI, produces `RedirectNotSupported` and is never evaluated as the terminal CORS response. Configure the supplied actual-request handler and each preflight factory handler not to follow redirects when redirect provenance matters; redirect loops and cancellation remain bounded transport failures.

CorsSpec does not synthesize cookies, authorization headers, or request bodies. A caller-provided handler may supply those as part of its own test setup; CorsSpec never includes them in diagnostics.

## Expectations

`CorsExpectation.Allowed()` expects the requested origin to be granted. Set `allowWildcardOrigin: true` when a non-credentialed contract intentionally accepts `Access-Control-Allow-Origin: *`. Set `requireVaryOrigin: true` when a policy varies its response by origin and the cache-safety marker is part of the contract. Credentialed contracts cannot use a wildcard expectation and fail before a request is sent when configured that way.

`CorsScenario.Origin` must be the canonical browser-serialized HTTP(S) origin, such as `https://app.example` without a trailing slash or explicit default port; the opaque `null` origin is also supported. Non-canonical casing, paths, queries, fragments, credentials, and unsupported schemes are rejected before I/O.

`CorsExpectation.Denied()` checks browser-relevant headers rather than a status code. A `200`, `204`, or `401` can all be a correct denied response if the CORS headers do not grant the scenario.

Allowed expectations can assert exposed response headers and preflight max-age:

```csharp
var expectation = CorsExpectation.Allowed(
    expectedExposedHeaders: new[] { "X-Request-Id" },
    expectedMaxAge: TimeSpan.FromMinutes(10));
```

## ASP.NET Core policies

Configure the application under test with its normal `AddCors` and `UseCors`/endpoint policy setup. Global middleware, named policies, and endpoint metadata are all observed through the supplied client. A successful contract proves the emitted CORS response for that scenario only; it does not inspect or rewrite the configured policy.

## Diagnostics

`CorsVerificationResult.Issues` uses stable failure categories such as `MethodRejected`, `RequestedHeaderRejected`, `CredentialsMismatch`, `MissingVaryOrigin`, `ExposedHeadersMismatch`, and `MaxAgeMismatch`. Messages explain the missing browser-visible dimension without copying response bodies, cookies, bearer tokens, authorization values, or arbitrary response headers.

## Network and privacy boundary

The origin is request metadata, not a destination. CorsSpec never resolves or contacts it. The actual request uses the `HttpClient` supplied by the caller; a generated preflight can use the clean handler factory described above so caller defaults and handler-added credentials do not reach `OPTIONS`. Factory preflights inherit the caller's `HttpClient.Timeout`; explicit cancellation remains prompt, and a timeout produces a bounded `NetworkFailure` without sending the actual request. Core verification has no independent network activity and can be fully offline with a local test host. After a response-level verdict, the optional `KeelMatrix.Telemetry` integration may request one aggregate activation signal per execution (one for a matrix, not per cell), including a failed CORS verdict. The exact payload fields are `event`, `tool`, `tool_version`, `telemetry_version`, `schema_version`, pseudonymous `project_hash` and `installation_hash`, `runtime`, `os`, `ci`, and `timestamp`. It excludes origins, hostnames, endpoint paths or identity, endpoint-identifying methods, request/response headers, cookies, credentials or tokens, response bodies, and diagnostics. Invalid contracts and no-response executions do not request activation. The signal is asynchronous and best-effort; construction, queue, network, or delivery failure cannot change the result. Suppress it with `KEELMATRIX_NO_TELEMETRY=1`; validation runs set that variable. See [PRIVACY.md](../PRIVACY.md) and the [maintained KeelMatrix.Telemetry privacy contract](https://github.com/KeelMatrix/Telemetry/blob/main/PRIVACY.md) for local state, opt-out, endpoint, and 90-day retention details.

## Security limitation

CORS does not establish authentication, authorization, CSRF protection, server-side request forgery safety, or network access control. Use the application's security tests for those properties.
