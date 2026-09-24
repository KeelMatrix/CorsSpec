# CorsSpec contracts

`KeelMatrix.CorsSpec` verifies observable CORS behavior through a caller-supplied `HttpClient`. It does not start an application or infer policy from ASP.NET Core internals.

## Simple and preflight requests

`GET`, `HEAD`, and `POST` scenarios with no non-safelisted requested header are executed as one actual request. Other methods or requested headers first receive an `OPTIONS` request with:

- `Origin` set to the scenario origin;
- `Access-Control-Request-Method` set to the scenario method;
- `Access-Control-Request-Headers` set to the normalized requested header names.

For an allowed preflight, CorsSpec checks the allow-origin, allow-methods, allow-headers, credentials, max-age when asserted, and required `Vary: Origin` behavior, then sends the actual request. For a denied preflight, the actual request is not sent when the preflight blocks access, matching browser behavior.

CorsSpec does not synthesize cookies, authorization headers, or request bodies. A caller-provided handler may supply those as part of its own test setup; CorsSpec never includes them in diagnostics.

## Expectations

`CorsExpectation.Allowed()` expects the requested origin to be granted. Set `allowWildcardOrigin: true` when a non-credentialed contract intentionally accepts `Access-Control-Allow-Origin: *`. Set `requireVaryOrigin: true` when a policy varies its response by origin and the cache-safety marker is part of the contract. Credentialed contracts cannot use a wildcard expectation and fail before a request is sent when configured that way.

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

The origin is request metadata, not a destination. CorsSpec never resolves or contacts it. The only requests are sent through the `HttpClient` supplied by the caller. The package has no telemetry and performs no background network activity.

## Security limitation

CORS does not establish authentication, authorization, CSRF protection, server-side request forgery safety, or network access control. Use the application's security tests for those properties.
