# CorsSpec contracts

`KeelMatrix.CorsSpec` verifies observable CORS behavior through a caller-supplied `HttpClient`. It does not start an application or infer policy from ASP.NET Core internals.

## Simple and preflight requests

Before classification and request construction, browser-standard request methods `DELETE`, `GET`, `HEAD`, `OPTIONS`, `POST`, and `PUT` are normalized to uppercase. `GET`, `HEAD`, and `POST` scenarios with no unsafe requested header value are executed as one actual request. Other methods or known unsafe effective headers first receive an `OPTIONS` request with:

- `Origin` set to the scenario origin;
- `Accept: */*` to match browser preflight construction;
- `Access-Control-Request-Method` set to the normalized browser-standard method or the exact-cased custom method;
- `Access-Control-Request-Headers` set to sorted lowercase unsafe header names, joined with commas and no added spaces.

For an allowed preflight, CorsSpec requires a 2xx response and checks the allow-origin, allow-methods, allow-headers, credentials, max-age when asserted, and required `Vary: Origin` behavior, then sends the actual request. A non-2xx response fails an allowed preflight even when its CORS headers are otherwise permissive. When a preflight is caused by requested headers, the safelisted methods `GET`, `HEAD`, and `POST` do not require a matching `Access-Control-Allow-Methods` token, but a present malformed method list still fails closed. Other methods require an exact allow-method match, including exact response-token casing; custom method tokens are also case-sensitive. For a non-credentialed contract, a valid method wildcard is accepted. A denied contract passes immediately when the preflight blocks access, but when preflight grants access CorsSpec executes and evaluates the actual response too.

Other valid method names preserve the caller's casing because custom methods remain case-sensitive. Browser-forbidden `CONNECT`, `TRACE`, and `TRACK` methods are rejected before a request is created.

### Supported transport and effective request

Create the caller-owned client through `CorsTransport.CreateClient`:

```csharp
using var client = CorsTransport.CreateClient(testServer.CreateHandler());
client.BaseAddress = new Uri("http://localhost");
var verifier = new CorsVerifier(client, () => testServer.CreateHandler());
```

Create this shared client once for ordinary integration tests and CorsSpec. You own and dispose it; CorsSpec does not own application startup. Already-created ordinary clients from `new HttpClient()`, `TestServer.CreateClient()` or `WebApplicationFactory.CreateClient()` return actionable `UnsupportedTransport` before verification host I/O, for either expectation and either constructor. Change client creation once, then configure its base address, defaults and timeout as usual.

For `WebApplicationFactory`, use `factory.Server.CreateHandler()`. This direct handler does not inherit `WebApplicationFactoryClientOptions`, `ConfigureClient` settings, or the automatic redirect/cookie handlers installed by `factory.CreateClient()`. Apply the client settings you need explicitly; keep generated preflights clean and automatic redirects disabled. This version supplies no host-observer adapter. These support limits do not preclude future host-specific adapters.

Supported terminal handlers are exact `HttpClientHandler`, `SocketsHttpHandler`, and the direct Microsoft ASP.NET Core TestHost `ClientHandler`. Subclasses and other opaque terminals are unsupported. Network handlers must set `AllowAutoRedirect = false`, have no automatic authentication credentials, and leave automatic decompression disabled. Declare authorization explicitly as an author header. For example:

```csharp
using var client = CorsTransport.CreateClient(new SocketsHttpHandler
{
    AllowAutoRedirect = false,
    UseCookies = false,
    RequestHeaderEncodingSelector = (_, _) => System.Text.Encoding.UTF8
});
client.BaseAddress = new Uri("http://localhost:5000");
```

Verification first runs the actual client's handler pipeline in a preparation pass. The installed boundary captures effective headers and returns a local response without calling the terminal handler or contacting the host. Default and handler-added author headers must be declared. Explicit `CorsRequestHeader` values override same-named defaults. A declared name without an observed value returns `UnsupportedRequest`; missing information never becomes a permission or denial verdict. Names-only values supplied by defaults or handlers are classified from their observed values.

Delegating handlers must support this extra preparation pass and preserve the scenario method, destination and origin. The final boundary checks those fields, effective values and encoding before execution and rejects observable request changes, retries and replaced responses. Handlers must be deterministic across preparation and execution; stateful changes to the operation are unsupported. Preparation is never a CORS verdict or telemetry activation.

The boundary captures the first response's status, request method/URI and CORS metadata by value before caller handlers regain control. Verification evaluates that immutable snapshot: `Access-Control-Allow-Origin`, `Access-Control-Allow-Methods`, `Access-Control-Allow-Headers`, `Access-Control-Allow-Credentials`, `Access-Control-Expose-Headers`, `Access-Control-Max-Age` and `Vary`. Later in-place additions, removals or replacements cannot change either verdict polarity, hide a redirect status or repair missing/changed original provenance. Such handler changes are ignored as CORS evidence; the result status is the first observed status. This applies to preflight and actual responses, including clean factory preflights. Response object identity remains a separate replacement/retry check. No body or arbitrary response header is copied into the snapshot. Original responses and content are disposed on completion, failure and cancellation; caller-owned clients remain caller-owned.

Headers use the raw, non-parsed representation observed at that boundary. Leading/trailing `SP`/`HTAB` are removed; internal HTAB is legal HTTP and safe in `Accept`, but outside the language-header safelist. Other C0 controls and `DEL` are rejected. Multiple .NET values for one name become one comma-space combined field before classification and serialization. If a handler parses/formats a content header, that formatted value is captured. Classification never uses a separate temporary formatted copy. Supported content headers are placed on request content.

The 128-byte safelist limit applies to each effective combined field. The 1024-byte aggregate rule applies to safelisted fields using the same selected encoding. For tracked sends, the boundary caches a canonical encoding per field and the network writer uses that cached choice; a changed selector is unsupported. `HttpClientHandler` and default `SocketsHttpHandler` support ASCII values. `SocketsHttpHandler.RequestHeaderEncodingSelector` can select ASCII, Latin-1 or UTF-8; values must be losslessly representable. Surrogate characters and other encodings are unsupported. Direct TestHost values use the UTF-8 byte contract. TestHost copies parsed .NET header values into the host. The boundary materializes those values before host I/O and requires a single value identical to the classified field; formatting changes or parsed multi-value fields return `UnsupportedRequest` for either expectation. Supply a canonical single .NET header value, such as `text/plain; charset=utf-8`, or use a supported network transport to preserve raw values. This includes formatter-sensitive `Accept`, language and content headers. A raw 128-byte `text/plain;charset=...` that TestHost would expand to 129 bytes is unsupported, while canonical 128/129-byte fields are classified and sent consistently. For example, 65 `é` characters are 65 Latin-1 bytes and 130 UTF-8 bytes, with different preflight decisions. Combined fields of 1024 and 1025 bytes both already exceed the per-field limit and require preflight. With the five safelisted names represented once at no more than 128 bytes each, the aggregate cannot cross 1024 in this supported representation; the aggregate rule remains applied. `Range` is normalized before parsing a single ASCII-digit nondecreasing range. `Content-Type` uses HTTP whitespace and token rules for its MIME essence.

Cookies are browser-managed credentials: never declare `Cookie` as an author header. Default/handler cookies are preserved for `useCredentials: true` actual requests and excluded from preflight classification. Cookies on non-credentialed requests or preflights are unsupported. A network cookie container must likewise contain no cookies for a non-credentialed or preflight target. `Authorization` is a declarable author header requiring preflight permission.

### Preflight transport isolation

When actual-client defaults or delegating handlers would add headers to `OPTIONS`, supply a clean factory with the second constructor. Each returned pipeline must satisfy the same supported terminal and no-auto-redirect contract. CorsSpec disposes it after the preflight and applies the actual client's timeout and cancellation token. Changed metadata, extra author headers or credentials fail explicitly at the final boundary. The one-argument constructor throws an actionable configuration exception when a preflight would inherit client defaults. Finite timeouts produce `NetworkFailure`; caller cancellation propagates. Request-sent flags describe attempts through `HttpClient`, not proof of host receipt.

Scenario targets must remain relative application paths. Raw C0 control characters (`U+0000` through `U+001F`) and `DEL` (`U+007F`) are rejected before a caller handler can receive a request; percent-encoded path data remains valid. URI authorities and schemes at the start of the reference, any backslash separator, UNC/device paths, and every slash-prefixed root-relative Windows drive-shaped path—including `/C:`, `/C:/orders`, `/C:orders`, nested segments, and suffixes containing query or fragment delimiters—are also rejected before I/O. Valid `/orders`, `orders`, and query-bearing relative targets remain relative, including raw scheme-like text such as `?next=https://external.example/orders`, `?next=/C:foo`, or `#next=https://external.example/orders`; only a scheme or authority at the start is rejected. Expected exposed response headers cannot include the forbidden `Set-Cookie` or `Set-Cookie2` names, including casing variants or duplicates; that impossible expectation fails before I/O.

For response exposure, the browser CORS safelist is honored for `Cache-Control`, `Content-Language`, `Content-Length`, `Content-Type`, `Expires`, `Last-Modified`, and `Pragma`; these assertions do not require `Access-Control-Expose-Headers`. Other asserted response headers require a complete, valid expose-header list. `Access-Control-Allow-Methods`, `Access-Control-Allow-Headers`, and `Access-Control-Expose-Headers` distinguish absent, valid-empty, wildcard, and explicit-member states. Empty members are valid list syntax; a valid empty method list does not block a preflighted safelisted method that needs no method-list match, and a valid empty header list does not block a request with no non-safelisted header match. Wildcard-plus-explicit members are parsed for all three list fields, while matching remains field-specific and credential-aware; wildcard-only allow-headers metadata does not cover `Authorization`, while an explicit `Authorization` member does. Required `Vary` lists tolerate empty members but reject non-token bytes and non-HTTP whitespace. Origin and wildcard values trim only HTTP optional whitespace (`SP` and `HTAB`); Unicode whitespace, controls, and obs-text at the boundary remain malformed. For credentialed scenarios, `Access-Control-Allow-Credentials` accepts one lowercase `true` value with HTTP optional whitespace around it; asserted `Access-Control-Max-Age` values are one ASCII digit-only value with optional HTTP whitespace around it. Malformed metadata never grants the permission that field is meant to establish.

Redirects are unsupported operations. The controlled boundary requires automatic redirects to be disabled before I/O; terminal URI/method equality is only an additional consistency check, never redirect-history evidence. A first 300, 301, 302, 303, 307 or 308 response produces `RedirectNotSupported` for either expectation, including self-redirects and chains returning to the initial URI. Both preflight paths and actual requests enforce that boundary and dispose rejected responses. A 304 response remains evaluable.

CorsSpec does not synthesize credentials or application request bodies. Caller handlers may supply credentials under the declared contract; their values never appear in diagnostics.

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

The origin is request metadata, not a destination. CorsSpec never resolves or contacts it. The actual request uses the `HttpClient` supplied by the caller; a generated preflight can use the clean handler factory described above so caller defaults and handler-added credentials do not reach `OPTIONS`. Factory preflights inherit the caller's `HttpClient.Timeout`; explicit cancellation remains prompt, and a timeout produces a bounded `NetworkFailure` without sending the actual request. Core verification has no independent network activity and can be fully offline with a local test host. A single-contract execution requests activation after an eligible response verdict. A completed matrix makes one aggregate decision and requests activation at most once when any cell reaches an eligible verdict. Failed CORS verdicts count; invalid/unsupported configurations and operations without a final response verdict do not. A matrix that propagates cancellation requests no activation, even after partial verdicts. CorsSpec calls only the parameterless shared `TrackActivation()` method. The call carries no CORS contract data. The shared package owns payload, opt-out, identity/state, deduplication, queueing, delivery, and failure handling. See [PRIVACY.md](../PRIVACY.md) and the [maintained KeelMatrix.Telemetry privacy contract](https://github.com/KeelMatrix/Telemetry/blob/main/PRIVACY.md); validation suppresses telemetry through the shared opt-out.

## Security limitation

CORS does not establish authentication, authorization, CSRF protection, server-side request forgery safety, or network access control. Use the application's security tests for those properties.

## Standards

Request safelisting and generated metadata follow the [Fetch Standard](https://fetch.spec.whatwg.org/). Header encoding follows the [SocketsHttpHandler encoding contract](https://learn.microsoft.com/en-us/dotnet/api/system.net.http.socketshttphandler.requestheaderencodingselector). The release validator intentionally accepts a flat subset of [CommonMark](https://spec.commonmark.org/0.31.2/), described in the [developer guide](DEV.md).
