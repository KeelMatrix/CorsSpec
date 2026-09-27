# CorsSpec contracts

`KeelMatrix.CorsSpec` verifies observable CORS behavior through a caller-supplied `HttpClient`. It does not start an application or infer policy from ASP.NET Core internals.

## Simple and preflight requests

Before classification and request construction, the case-insensitive browser methods `DELETE`, `GET`, `HEAD`, `OPTIONS`, `POST`, and `PUT` are normalized to uppercase. `GET`, `HEAD`, and `POST` scenarios with no requested header name are executed as one actual request. Other methods or requested headers first receive an `OPTIONS` request with:

- `Origin` set to the scenario origin;
- `Accept: */*` to match browser preflight construction;
- `Access-Control-Request-Method` set to the normalized browser-standard method or the exact-cased custom method;
- `Access-Control-Request-Headers` set to the normalized requested header names.

For an allowed preflight, CorsSpec requires a 2xx response and checks the allow-origin, allow-methods, allow-headers, credentials, max-age when asserted, and required `Vary: Origin` behavior, then sends the actual request. A non-2xx response fails an allowed preflight even when its CORS headers are otherwise permissive. When a preflight is caused by requested headers, the safelisted methods `GET`, `HEAD`, and `POST` do not require a matching `Access-Control-Allow-Methods` token, but a present malformed method list still fails closed. Other methods require an exact allow-method match; normalized browser-standard names use their normalized form, while custom method tokens are case-sensitive. For a non-credentialed contract, a valid method wildcard is also accepted. For a denied preflight, the actual request is not sent when the preflight blocks access, matching browser behavior.

Other valid method names preserve the caller's casing because custom methods remain case-sensitive. Browser-forbidden `CONNECT`, `TRACE`, and `TRACK` methods are rejected before a request is created.

The scenario API accepts header names only, not header values. Because it cannot model value-sensitive safelisting, every requested caller-added header name conservatively forces a preflight, including `Accept`, `Accept-Language`, `Content-Language`, `Content-Type`, and `Range`, regardless of whether its value is safe or unsafe. Include names supplied through `HttpClient.DefaultRequestHeaders` and through per-request/delegating-handler setup; a handler that adds an unlisted header is outside the scenario model and must be corrected by the caller. Browser-managed, forbidden, and CORS protocol header names are rejected before I/O, including the conditional override family `X-HTTP-Method`, `X-HTTP-Method-Override`, and `X-Method-Override`; CorsSpec rejects these names because it does not model their value-dependent browser rule.

Scenario targets must remain relative application paths. Raw C0 control characters (`U+0000` through `U+001F`) and `DEL` (`U+007F`) are rejected before a caller handler can receive a request; percent-encoded path data remains valid. URI authorities and schemes, any backslash separator, UNC/device paths, and root-relative Windows paths are also rejected before I/O. Valid `/orders`, `orders`, and query-bearing relative targets remain relative. Expected exposed response headers cannot include the forbidden `Set-Cookie` or `Set-Cookie2` names, including casing variants or duplicates; that impossible expectation fails before I/O.

For response exposure, the browser CORS safelist is honored for `Cache-Control`, `Content-Language`, `Content-Length`, `Content-Type`, `Expires`, `Last-Modified`, and `Pragma`; these assertions do not require `Access-Control-Expose-Headers`. Other asserted response headers require a complete, valid expose-header list. Allow-method, allow-header, and expose-header lists reject empty members, invalid token bytes, and wildcard-plus-extra-token forms instead of matching a valid sibling. For credentialed scenarios, `Access-Control-Allow-Credentials` remains one exact lowercase `true` value; asserted `Access-Control-Max-Age` values are one ASCII digit-only value with optional HTTP whitespace around it. Malformed metadata never grants the permission that field is meant to establish.

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

The origin is request metadata, not a destination. CorsSpec never resolves or contacts it. The scenario path, `Origin`, and preflight metadata are intentionally sent through the `HttpClient` supplied by the caller. The caller controls that client's routing and any caller-added headers. CorsSpec has no telemetry or independent/background network activity and does not copy response bodies, credentials, or diagnostics to a CorsSpec service.

## Security limitation

CORS does not establish authentication, authorization, CSRF protection, server-side request forgery safety, or network access control. Use the application's security tests for those properties.
