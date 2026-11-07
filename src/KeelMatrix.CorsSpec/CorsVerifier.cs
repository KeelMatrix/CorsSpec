using System.Net;

namespace KeelMatrix.CorsSpec;

/// <summary>Executes CORS contracts through a caller-owned <see cref="HttpClient"/> created by <see cref="CorsTransport.CreateClient(HttpMessageHandler)"/>.</summary>
public sealed class CorsVerifier
{
    private readonly HttpClient _client;
    private readonly Func<HttpMessageHandler>? _preflightHandlerFactory;
    private readonly ICorsTelemetry _telemetry;
    private readonly bool _allowTestHandler;

    /// <summary>Creates a verifier over a caller-owned client created by CorsTransport.CreateClient.</summary>
    /// <remarks>Already-created ordinary clients, including TestServer.CreateClient, WebApplicationFactory.CreateClient and new HttpClient, return UnsupportedTransport before verification host I/O. For a preflighted scenario, this overload fails closed when the client has default request headers. Use the handler-factory overload when caller defaults or handler-added headers must remain on the actual request only.</remarks>
    public CorsVerifier(HttpClient client)
        : this(client, null, new SharedTelemetry())
    {
    }

    /// <summary>Creates a verifier that uses the supplied client for actual requests and a dedicated handler for generated preflights.</summary>
    /// <param name="client">The caller-owned client created by CorsTransport.CreateClient, with its base address and actual-request settings.</param>
    /// <param name="preflightHandlerFactory">Creates a clean handler for each generated preflight. The handler must not add caller-wide or credential headers; the verifier disposes each returned handler after the preflight completes and applies the supplied client's timeout and cancellation boundary.</param>
    public CorsVerifier(HttpClient client, Func<HttpMessageHandler> preflightHandlerFactory)
        : this(client, preflightHandlerFactory, new SharedTelemetry())
    {
    }

    internal CorsVerifier(HttpClient client, ICorsTelemetry telemetry)
        : this(client, null, telemetry)
    {
    }

    internal CorsVerifier(
        HttpClient client,
        Func<HttpMessageHandler>? preflightHandlerFactory,
        ICorsTelemetry telemetry)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _preflightHandlerFactory = preflightHandlerFactory;
        _allowTestHandler = CorsTransport.AllowsTestHandler(client);
        _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
    }

    /// <summary>Executes one contract and evaluates the immutable first-response CORS evidence captured at the transport boundary.</summary>
    public async Task<CorsVerificationResult> VerifyAsync(CorsContract contract, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(contract);

        var result = await VerifyCoreAsync(contract, cancellationToken).ConfigureAwait(false);
        TrackActivationIfVerdict(result);
        return result;
    }

    private async Task<CorsVerificationResult> VerifyCoreAsync(CorsContract contract, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var scenario = contract.Scenario;
        var expectation = contract.Expectation;
        var issues = new List<CorsIssue>();
        if (!CorsTransport.IsSupportedClient(_client))
        {
            issues.Add(new CorsIssue(CorsFailureKind.UnsupportedTransport,
                "Create the shared integration-test client with CorsTransport.CreateClient over a supported direct host or network handler, then set its BaseAddress, defaults and timeout. Already-created ordinary clients cannot expose effective headers and the first response to this verifier."));
            return CreateResult(contract, false, false, null, false, null, issues);
        }

        var earlyIssues = CorsHeaderEvaluator.ValidateContract(contract, requiresPreflight: true);
        if (earlyIssues.Count != 0)
        {
            return new CorsVerificationResult(contract, false, false, null, false, null, earlyIssues);
        }

        EffectiveCorsRequest effective;
        try
        {
            using var preparation = CreateActualRequest(scenario);
            using var context = new CorsSendContext(scenario, prepare: true) { Target = ResolveExpectedUri(preparation.RequestUri) };
            preparation.Options.Set(CorsSendContext.Key, context);
            using var preparedResponse = await _client.SendAsync(preparation, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            EnsureBoundary(context, preparedResponse);
            effective = context.Effective!;
        }
        catch (UnsupportedCorsRequestException exception)
        {
            issues.Add(new CorsIssue(exception.Kind, exception.Message));
            return CreateResult(contract, false, false, null, false, null, issues);
        }
        catch (ArgumentException)
        {
            issues.Add(new CorsIssue(CorsFailureKind.UnsupportedRequest, "The effective request contains invalid header data."));
            return CreateResult(contract, false, false, null, false, null, issues);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            issues.Add(new CorsIssue(CorsFailureKind.NetworkFailure, "The request handler pipeline timed out during preparation."));
            return CreateResult(contract, false, false, null, false, null, issues);
        }
        catch (HttpRequestException)
        {
            issues.Add(new CorsIssue(CorsFailureKind.NetworkFailure, "The request handler pipeline could not prepare the CORS operation."));
            return CreateResult(contract, false, false, null, false, null, issues);
        }

        var preflightHeaderNames = effective.PreflightNames;
        var requiresPreflight = scenario.RequiresPreflight(preflightHeaderNames);
        var localIssues = CorsHeaderEvaluator.ValidateContract(contract, requiresPreflight);
        if (localIssues.Count != 0)
        {
            return new CorsVerificationResult(contract, false, false, null, false, null, localIssues);
        }

        var preflightSent = false;
        HttpStatusCode? preflightStatusCode = null;
        var actualRequestSent = false;
        HttpStatusCode? actualStatusCode = null;

        if (requiresPreflight)
        {
            preflightSent = true;
            HttpResponseMessage? response = null;
            try
            {
                using var preflight = CreatePreflightRequest(scenario, preflightHeaderNames);
                using var context = CreatePreflightContext(preflight, scenario);
                response = await SendPreflightAsync(preflight, context, cancellationToken).ConfigureAwait(false);
                using var firstResponse = context.FirstResponse!.CreateEvaluationResponse();
                preflightStatusCode = firstResponse.StatusCode;
                var preflightEvaluation = CorsHeaderEvaluator.EvaluatePreflight(firstResponse, scenario, expectation, preflightHeaderNames);
                issues.AddRange(preflightEvaluation.Issues);

                if (!expectation.IsAllowed)
                {
                    if (!preflightEvaluation.GrantsCorsAccess)
                    {
                        return CreateResult(contract, true, preflightSent, preflightStatusCode, false, null, issues);
                    }

                    // A granted preflight is permission to attempt the browser operation, not its final CORS
                    // verdict. Continue so a denied contract can assert the actual response as well.
                }

                if (!preflightEvaluation.GrantsCorsAccess)
                {
                    return CreateResult(contract, false, preflightSent, preflightStatusCode, false, null, issues);
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                issues.Add(new CorsIssue(CorsFailureKind.NetworkFailure, "The CORS preflight timed out in the caller-supplied HttpClient."));
                return CreateResult(contract, false, preflightSent, preflightStatusCode, false, null, issues);
            }
            catch (HttpRequestException)
            {
                issues.Add(new CorsIssue(CorsFailureKind.NetworkFailure, "The caller-supplied HttpClient could not complete the CORS preflight."));
                return CreateResult(contract, false, preflightSent, preflightStatusCode, false, null, issues);
            }
            catch (UnsupportedCorsRequestException exception)
            {
                issues.Add(new CorsIssue(exception.Kind, exception.Message));
                return CreateResult(contract, false, preflightSent, preflightStatusCode, false, null, issues);
            }
            catch (RedirectNotSupportedException exception)
            {
                issues.Add(new CorsIssue(CorsFailureKind.RedirectNotSupported, exception.Message));
                return CreateResult(contract, false, preflightSent, preflightStatusCode, false, null, issues);
            }
            finally
            {
                response?.Dispose();
            }
        }

        using var actual = CreateActualRequest(scenario);
        HttpResponseMessage? actualResponse = null;
        try
        {
            var expectedRequest = CaptureRequestIdentity(actual);
            using var context = new CorsSendContext(scenario, prepare: false) { Expected = effective.Identity, Target = ResolveExpectedUri(actual.RequestUri) };
            actual.Options.Set(CorsSendContext.Key, context);
            actualRequestSent = true;
            actualResponse = await _client.SendAsync(actual, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            EnsureBoundary(context, actualResponse);
            EnsureNoRedirect(expectedRequest, context.FirstResponse!);
            using var firstResponse = context.FirstResponse!.CreateEvaluationResponse();
            actualStatusCode = firstResponse.StatusCode;
            var actualEvaluation = CorsHeaderEvaluator.EvaluateActual(firstResponse, scenario, expectation);
            issues.AddRange(actualEvaluation.Issues);

            if (!expectation.IsAllowed && actualEvaluation.GrantsCorsAccess)
            {
                issues.Add(new CorsIssue(
                    CorsFailureKind.UnexpectedCorsPermission,
                    "The actual response granted the requested origin, but the contract expected access to be denied."));
            }

            if (!expectation.IsAllowed && !actualEvaluation.GrantsCorsAccess)
            {
                return CreateResult(contract, true, preflightSent, preflightStatusCode, actualRequestSent, actualStatusCode, issues);
            }

            return CreateResult(contract, issues.Count == 0, preflightSent, preflightStatusCode, actualRequestSent, actualStatusCode, issues);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            issues.Add(new CorsIssue(CorsFailureKind.NetworkFailure, "The actual CORS request timed out in the caller-supplied HttpClient."));
        }
        catch (HttpRequestException)
        {
            issues.Add(new CorsIssue(CorsFailureKind.NetworkFailure, "The caller-supplied HttpClient could not complete the actual CORS request."));
        }
        catch (UnsupportedCorsRequestException exception)
        {
            issues.Add(new CorsIssue(exception.Kind, exception.Message));
        }
        catch (RedirectNotSupportedException exception)
        {
            issues.Add(new CorsIssue(CorsFailureKind.RedirectNotSupported, exception.Message));
        }
        finally
        {
            actualResponse?.Dispose();
        }

        return CreateResult(contract, false, preflightSent, preflightStatusCode, actualRequestSent, actualStatusCode, issues);
    }

    private async Task<HttpResponseMessage> SendPreflightAsync(HttpRequestMessage preflight, CorsSendContext context, CancellationToken cancellationToken)
    {
        if (_preflightHandlerFactory is null)
        {
            if (_client.DefaultRequestHeaders.Any())
            {
                throw new InvalidOperationException(
                    "Preflight verification cannot use a client with default request headers because they would be sent with the OPTIONS request. Supply a dedicated preflight handler factory to CorsVerifier.");
            }

            var expectedRequest = CaptureRequestIdentity(preflight);
            var response = await _client.SendAsync(preflight, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            try
            {
                EnsureBoundary(context, response);
                EnsureNoRedirect(expectedRequest, context.FirstResponse!);
                return response;
            }
            catch
            {
                response.Dispose();
                throw;
            }
        }

        var handler = _preflightHandlerFactory();
        if (handler is null)
        {
            throw new InvalidOperationException("The preflight handler factory returned null.");
        }

        using var preflightClient = CorsTransport.CreateClient(handler, allowTestHandler: _allowTestHandler);
        preflightClient.Timeout = _client.Timeout;
        preflight.RequestUri = ResolvePreflightUri(preflight.RequestUri);
        var factoryExpectedRequest = CaptureRequestIdentity(preflight);
        var factoryResponse = await preflightClient.SendAsync(preflight, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureBoundary(context, factoryResponse);
            EnsureNoRedirect(factoryExpectedRequest, context.FirstResponse!);
            return factoryResponse;
        }
        catch
        {
            factoryResponse.Dispose();
            throw;
        }
    }

    private Uri ResolvePreflightUri(Uri? relativeUri)
    {
        if (relativeUri is null || relativeUri.IsAbsoluteUri || _client.BaseAddress is null)
        {
            throw new InvalidOperationException("The caller-supplied HttpClient must have an absolute BaseAddress for preflight verification.");
        }

        return new Uri(_client.BaseAddress, relativeUri);
    }

    private ExpectedRequestIdentity CaptureRequestIdentity(HttpRequestMessage request) =>
        new(request.Method.Method, ResolveExpectedUri(request.RequestUri));

    private Uri? ResolveExpectedUri(Uri? requestUri)
    {
        if (requestUri is null || requestUri.IsAbsoluteUri || _client.BaseAddress is null)
        {
            return requestUri;
        }

        return new Uri(_client.BaseAddress, requestUri);
    }

    private static void EnsureNoRedirect(ExpectedRequestIdentity expectedRequest, FirstCorsResponse response)
    {
        if (IsRedirectStatusCode(response.StatusCode))
        {
            throw new RedirectNotSupportedException("The CORS verifier does not follow redirects; the supplied transport returned a redirect response.");
        }

        if (response.RequestMethod is null || response.RequestUri is null)
        {
            throw new RedirectNotSupportedException("The supplied transport did not expose request provenance, so the response cannot be attributed to the requested CORS operation.");
        }

        if (!string.Equals(expectedRequest.Method, response.RequestMethod, StringComparison.Ordinal) ||
            expectedRequest.Uri is null ||
            !UriEquals(expectedRequest.Uri, response.RequestUri))
        {
            throw new RedirectNotSupportedException("The supplied HttpClient followed a redirect, so the response cannot be attributed to the requested CORS operation.");
        }
    }

    private static bool IsRedirectStatusCode(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.MultipleChoices or
            HttpStatusCode.MovedPermanently or
            HttpStatusCode.Found or
            HttpStatusCode.SeeOther or
            HttpStatusCode.TemporaryRedirect or
            HttpStatusCode.PermanentRedirect;

    private static bool UriEquals(Uri left, Uri right) =>
        Uri.Compare(left, right, UriComponents.HttpRequestUrl, UriFormat.UriEscaped, StringComparison.Ordinal) == 0;

    /// <summary>Executes the contracts in a bounded matrix in declaration order.</summary>
    public async Task<IReadOnlyList<CorsVerificationResult>> VerifyMatrixAsync(CorsMatrix matrix, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(matrix);
        var results = new List<CorsVerificationResult>(matrix.Contracts.Count);
        foreach (var contract in matrix.Contracts)
        {
            var result = await VerifyCoreAsync(contract, cancellationToken).ConfigureAwait(false);
            results.Add(result);
        }

        // Cancellation aborts the matrix without requesting activation, including partial verdicts.
        if (results.Any(static result => result.HasVerdict))
        {
            _telemetry.TrackActivation();
        }

        return results.AsReadOnly();
    }

    private void TrackActivationIfVerdict(CorsVerificationResult result)
    {
        if (result.HasVerdict)
        {
            _telemetry.TrackActivation();
        }
    }

    private CorsSendContext CreatePreflightContext(HttpRequestMessage request, CorsScenario scenario)
    {
        var effective = EffectiveCorsRequest.Capture(request, scenario, static _ => System.Text.Encoding.UTF8, preflight: true);
        // Compare normalized metadata independently of the handler encoding.
        var context = new CorsSendContext(scenario, prepare: false, preflight: true) { Target = ResolveExpectedUri(request.RequestUri) };
        request.Options.Set(CorsSendContext.Key, context);
        context.PreflightHeaders = effective.HeaderValues;
        return context;
    }

    private static void EnsureBoundary(CorsSendContext context, HttpResponseMessage response)
    {
        if (context.Calls != 1 || !ReferenceEquals(context.Response, response) || context.Effective is null ||
            (!context.Prepare && context.FirstResponse is null))
        {
            context.Response?.Dispose();
            throw new UnsupportedCorsRequestException("The supplied handler pipeline did not return the first observed response.", CorsFailureKind.RedirectNotSupported);
        }
    }

    internal static HttpRequestMessage CreatePreflightRequest(CorsScenario scenario, IReadOnlyList<string> preflightHeaderNames)
    {
        var request = new HttpRequestMessage(HttpMethod.Options, scenario.Path);
        AddOrigin(request, scenario.Origin);
        request.Headers.TryAddWithoutValidation("Accept", "*/*");
        request.Headers.TryAddWithoutValidation("Access-Control-Request-Method", scenario.Method.Method);
        if (preflightHeaderNames.Count != 0)
        {
            request.Headers.TryAddWithoutValidation(
                "Access-Control-Request-Headers",
                string.Join(",", preflightHeaderNames.Select(static header => header.ToLowerInvariant())));
        }

        return request;
    }

    internal static HttpRequestMessage CreateActualRequest(CorsScenario scenario)
    {
        var request = new HttpRequestMessage(scenario.Method, scenario.Path);
        AddOrigin(request, scenario.Origin);
        foreach (var header in scenario.RequestHeaders)
        {
            if (!request.Headers.TryAddWithoutValidation(header.Name, header.Value))
            {
                request.Content ??= new ByteArrayContent(Array.Empty<byte>());
                if (!request.Content.Headers.TryAddWithoutValidation(header.Name, header.Value))
                {
                    throw new ArgumentException($"The request header '{header.Name}' could not be serialized.", nameof(scenario));
                }
            }
        }

        return request;
    }

    private static void AddOrigin(HttpRequestMessage request, string origin)
    {
        request.Headers.TryAddWithoutValidation("Origin", origin);
    }

    private static CorsVerificationResult CreateResult(
        CorsContract contract,
        bool isSuccess,
        bool preflightSent,
        HttpStatusCode? preflightStatusCode,
        bool actualRequestSent,
        HttpStatusCode? actualStatusCode,
        List<CorsIssue> issues) =>
        new(contract, isSuccess, preflightSent, preflightStatusCode, actualRequestSent, actualStatusCode, issues.ToArray());

    private readonly record struct ExpectedRequestIdentity(string Method, Uri? Uri);
}

internal readonly record struct CorsEvaluation(bool GrantsCorsAccess, IReadOnlyList<CorsIssue> Issues);

internal static class CorsHeaderEvaluator
{
    private const string AllowOrigin = "Access-Control-Allow-Origin";
    private const string AllowMethods = "Access-Control-Allow-Methods";
    private const string AllowHeaders = "Access-Control-Allow-Headers";
    private const string AllowCredentials = "Access-Control-Allow-Credentials";
    private const string ExposeHeaders = "Access-Control-Expose-Headers";
    private const string MaxAge = "Access-Control-Max-Age";
    private static readonly string[] CorsSafelistedResponseHeaders =
    [
        "Cache-Control",
        "Content-Language",
        "Content-Length",
        "Content-Type",
        "Expires",
        "Last-Modified",
        "Pragma"
    ];

    public static IReadOnlyList<CorsIssue> ValidateContract(CorsContract contract, bool requiresPreflight)
    {
        var issues = new List<CorsIssue>();
        if (contract.Scenario.UseCredentials && contract.Expectation.AllowWildcardOrigin)
        {
            issues.Add(new CorsIssue(
                CorsFailureKind.MalformedScenario,
                "A credentialed scenario cannot require a wildcard allow-origin response."));
        }

        if (!requiresPreflight && contract.Expectation.ExpectedMaxAge is not null)
        {
            issues.Add(new CorsIssue(
                CorsFailureKind.MalformedScenario,
                "An expected preflight max-age requires a scenario that performs a preflight."));
        }

        if (!contract.Expectation.IsAllowed && (contract.Expectation.ExpectedExposedHeaders.Count != 0 || contract.Expectation.ExpectedMaxAge is not null))
        {
            issues.Add(new CorsIssue(
                CorsFailureKind.MalformedScenario,
                "Exposed-header and max-age assertions require an allowed expectation."));
        }

        return issues;
    }

    public static CorsEvaluation EvaluatePreflight(
        HttpResponseMessage response,
        CorsScenario scenario,
        CorsExpectation expectation,
        IReadOnlyList<string> preflightHeaderNames)
    {
        var issues = new List<CorsIssue>();
        var originPermission = EvaluateOrigin(response, scenario, expectation, issues);
        var methodPermission = HasMethodPermission(response, scenario);
        var rawHeaders = GetValues(response, AllowHeaders);
        var headersMetadataValid = rawHeaders.Count == 0 || TryParseCorsList(rawHeaders, AllowHeaders, out _);
        var headersPermission = headersMetadataValid && preflightHeaderNames.All(header => HasToken(response, AllowHeaders, header, !scenario.UseCredentials));
        if (!headersMetadataValid)
        {
            issues.Add(new CorsIssue(CorsFailureKind.RequestedHeaderRejected, "The response contained malformed Access-Control-Allow-Headers metadata."));
        }
        var statusPermission = response.IsSuccessStatusCode;

        if (expectation.IsAllowed && !statusPermission)
        {
            issues.Add(new CorsIssue(CorsFailureKind.PreflightStatusRejected, "The preflight response did not return an HTTP 2xx status required for browser CORS permission."));
        }

        if (expectation.IsAllowed)
        {
            if (!methodPermission)
            {
                issues.Add(new CorsIssue(CorsFailureKind.MethodRejected, $"The preflight response did not permit method {scenario.Method.Method}; HTTP status is not the CORS verdict."));
            }

            if (!headersPermission)
            {
                issues.Add(new CorsIssue(CorsFailureKind.RequestedHeaderRejected, "The preflight response did not permit every requested header; HTTP status is not the CORS verdict."));
            }

            EvaluateCredentialAndVary(response, scenario, expectation, originPermission, issues);
            EvaluateMaxAge(response, expectation, issues);
            return new CorsEvaluation(statusPermission && originPermission && methodPermission && headersPermission && HasCredentialPermission(response, scenario), issues.ToArray());
        }

        var grants = statusPermission && originPermission && methodPermission && headersPermission && HasCredentialPermission(response, scenario);
        return new CorsEvaluation(grants, issues.ToArray());
    }

    public static CorsEvaluation EvaluateActual(HttpResponseMessage response, CorsScenario scenario, CorsExpectation expectation)
    {
        var issues = new List<CorsIssue>();
        var originPermission = EvaluateOrigin(response, scenario, expectation, issues);
        var credentialsPermission = HasCredentialPermission(response, scenario);

        if (expectation.IsAllowed)
        {
            EvaluateCredentialAndVary(response, scenario, expectation, originPermission, issues);
            EvaluateExposedHeaders(response, scenario, expectation, issues);
            return new CorsEvaluation(originPermission && credentialsPermission, issues.ToArray());
        }

        return new CorsEvaluation(originPermission && credentialsPermission, issues.ToArray());
    }

    private static bool EvaluateOrigin(HttpResponseMessage response, CorsScenario scenario, CorsExpectation expectation, List<CorsIssue> issues)
    {
        var values = GetValues(response, AllowOrigin);
        if (values.Count != 1)
        {
            if (expectation.IsAllowed)
            {
                issues.Add(new CorsIssue(CorsFailureKind.MissingOrMismatchedAllowOrigin, "The response did not contain exactly one Access-Control-Allow-Origin value matching the requested origin."));
            }

            return false;
        }

        var value = TrimHttpOws(values[0]);
        if (value == "*" && !scenario.UseCredentials)
        {
            if (expectation.IsAllowed && !expectation.AllowWildcardOrigin)
            {
                issues.Add(new CorsIssue(CorsFailureKind.MissingOrMismatchedAllowOrigin, "Access-Control-Allow-Origin used a wildcard without an explicit wildcard expectation."));
            }

            return true;
        }

        if (string.Equals(value, scenario.Origin, StringComparison.Ordinal))
        {
            return true;
        }

        if (expectation.IsAllowed)
        {
            issues.Add(new CorsIssue(CorsFailureKind.MissingOrMismatchedAllowOrigin, "Access-Control-Allow-Origin did not match the requested origin or the allowed wildcard expectation."));
        }

        return false;
    }

    private static void EvaluateCredentialAndVary(
        HttpResponseMessage response,
        CorsScenario scenario,
        CorsExpectation expectation,
        bool originPermission,
        List<CorsIssue> issues)
    {
        if (scenario.UseCredentials && !HasCredentialPermission(response, scenario))
        {
            issues.Add(new CorsIssue(CorsFailureKind.CredentialsMismatch, "The credentialed scenario did not receive Access-Control-Allow-Credentials: true."));
        }

        if (expectation.RequireVaryOrigin && originPermission && !IsWildcardOrigin(response))
        {
            if (!HasVaryOrigin(response))
            {
                issues.Add(new CorsIssue(CorsFailureKind.MissingVaryOrigin, "The exact-origin response did not include Vary: Origin."));
            }
        }
    }

    private static void EvaluateExposedHeaders(HttpResponseMessage response, CorsScenario scenario, CorsExpectation expectation, List<CorsIssue> issues)
    {
        var rawValues = GetValues(response, ExposeHeaders);
        if (rawValues.Count != 0 && !TryParseCorsList(rawValues, ExposeHeaders, out _))
        {
            issues.Add(new CorsIssue(CorsFailureKind.ExposedHeadersMismatch, "The response contained malformed Access-Control-Expose-Headers metadata."));
            return;
        }

        if (expectation.ExpectedExposedHeaders.Count == 0)
        {
            return;
        }

        if (expectation.ExpectedExposedHeaders.Any(header =>
            !IsCorsSafelistedResponseHeader(header) &&
            !HasToken(response, ExposeHeaders, header, !scenario.UseCredentials)))
        {
            issues.Add(new CorsIssue(CorsFailureKind.ExposedHeadersMismatch, "The response did not expose every asserted response header."));
        }
    }

    private static void EvaluateMaxAge(HttpResponseMessage response, CorsExpectation expectation, List<CorsIssue> issues)
    {
        if (expectation.ExpectedMaxAge is not { } expected)
        {
            return;
        }

        var values = GetValues(response, MaxAge);
        if (values.Count != 1 || !TryParseDeltaSeconds(values[0], out var seconds) || seconds != (long)expected.TotalSeconds)
        {
            issues.Add(new CorsIssue(CorsFailureKind.MaxAgeMismatch, "The preflight response did not contain the asserted Access-Control-Max-Age value."));
        }
    }

    private static bool HasCredentialPermission(HttpResponseMessage response, CorsScenario scenario)
    {
        if (!scenario.UseCredentials)
        {
            return true;
        }

        var values = GetValues(response, AllowCredentials);
        return values.Count == 1 &&
               TrimHttpOws(values[0]).Equals("true", StringComparison.Ordinal) &&
               !IsWildcardOrigin(response);
    }

    private static bool IsWildcardOrigin(HttpResponseMessage response) =>
        GetValues(response, AllowOrigin).Count == 1 && TrimHttpOws(GetValues(response, AllowOrigin)[0]) == "*";

    private static bool HasToken(HttpResponseMessage response, string headerName, string expected, bool allowWildcard)
    {
        if (headerName == ExposeHeaders && IsCorsNonWildcardName(headerName, expected))
        {
            return false;
        }

        if (!TryParseCorsList(response, headerName, out var values))
        {
            return false;
        }

        return values.Any(value => value.Equals(expected, StringComparison.OrdinalIgnoreCase)) ||
            (allowWildcard && values.Contains("*", StringComparer.Ordinal) && !IsCorsNonWildcardName(headerName, expected));
    }

    private static bool HasMethodPermission(HttpResponseMessage response, CorsScenario scenario)
    {
        var rawValues = GetValues(response, AllowMethods);
        if (rawValues.Count == 0)
        {
            return CorsScenario.IsSimpleMethod(scenario.Method);
        }

        if (!TryParseCorsList(rawValues, AllowMethods, out var values))
        {
            return false;
        }

        if (CorsScenario.IsSimpleMethod(scenario.Method))
        {
            return true;
        }

        return values.Any(value => value.Equals(scenario.Method.Method, StringComparison.Ordinal)) ||
            (!scenario.UseCredentials && values.Contains("*", StringComparer.Ordinal));
    }

    private static bool IsCorsSafelistedResponseHeader(string expected) =>
        CorsSafelistedResponseHeaders.Any(header => header.Equals(expected, StringComparison.OrdinalIgnoreCase));

    private static bool TryParseCorsList(HttpResponseMessage response, string headerName, out IReadOnlyList<string> tokens)
    {
        return TryParseCorsList(GetValues(response, headerName), headerName, out tokens);
    }

    private static bool TryParseCorsList(IReadOnlyList<string> values, string headerName, out IReadOnlyList<string> tokens)
        => TryParseHttpTokenList(
            values,
            out tokens,
            allowWildcardMembers: headerName == AllowHeaders || headerName == AllowMethods || headerName == ExposeHeaders);

    private static bool TryParseDeltaSeconds(string value, out long seconds)
    {
        value = TrimHttpOws(value);
        if (value.Length == 0)
        {
            seconds = 0;
            return false;
        }

        seconds = 0;
        foreach (var character in value)
        {
            if (character is < '0' or > '9')
            {
                seconds = 0;
                return false;
            }

            var digit = character - '0';
            if (seconds > (long.MaxValue - digit) / 10)
            {
                seconds = 0;
                return false;
            }

            seconds = seconds * 10 + digit;
        }

        return true;
    }

    private static bool HasVaryOrigin(HttpResponseMessage response)
    {
        if (!TryParseHttpTokenList(GetValues(response, "Vary"), out var values))
        {
            return false;
        }

        return values.Any(static value => value.Equals("Origin", StringComparison.OrdinalIgnoreCase));
    }

    private static string TrimHttpOws(string value)
    {
        var start = 0;
        var end = value.Length;
        while (start < end && value[start] is ' ' or '\t')
        {
            start++;
        }

        while (end > start && value[end - 1] is ' ' or '\t')
        {
            end--;
        }

        return value[start..end];
    }

    private static bool TryParseHttpTokenList(IReadOnlyList<string> values, out IReadOnlyList<string> tokens, bool allowWildcardMembers = false)
    {
        var parsed = new List<string>();
        foreach (var value in values)
        {
            var memberStart = 0;
            for (var index = 0; index <= value.Length; index++)
            {
                if (index != value.Length && value[index] != ',')
                {
                    continue;
                }

                var member = TrimHttpOws(value[memberStart..index]);
                if (member.Length == 0)
                {
                    memberStart = index + 1;
                    continue;
                }

                if (!IsHttpToken(member))
                {
                    tokens = Array.Empty<string>();
                    return false;
                }

                parsed.Add(member);
                memberStart = index + 1;
            }
        }

        if (values.Count == 0 || (!allowWildcardMembers && parsed.Count > 1 && parsed.Contains("*", StringComparer.Ordinal)))
        {
            tokens = Array.Empty<string>();
            return false;
        }

        tokens = parsed;
        return true;
    }

    private static bool IsHttpToken(string value)
    {
        if (value.Length == 0)
        {
            return false;
        }

        foreach (var character in value)
        {
            if (character <= 0x7f &&
                (character is >= '0' and <= '9' ||
                 character is >= 'A' and <= 'Z' ||
                 character is >= 'a' and <= 'z' ||
                 "!#$%&'*+-.^_`|~".Contains(character)))
            {
                continue;
            }

            return false;
        }

        return true;
    }

    private static bool IsCorsNonWildcardName(string headerName, string expected) =>
        (headerName == AllowHeaders && expected.Equals("Authorization", StringComparison.OrdinalIgnoreCase)) ||
        (headerName == ExposeHeaders && (expected.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase) || expected.Equals("Set-Cookie2", StringComparison.OrdinalIgnoreCase)));

    private static List<string> GetValues(HttpResponseMessage response, string headerName)
    {
        if (response.Headers.TryGetValues(headerName, out var values))
        {
            return values.ToList();
        }

        return new List<string>();
    }
}

internal sealed class RedirectNotSupportedException : Exception
{
    public RedirectNotSupportedException(string message)
        : base(message)
    {
    }
}
