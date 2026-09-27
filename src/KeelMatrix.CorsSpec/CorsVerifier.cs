using System.Net;

namespace KeelMatrix.CorsSpec;

/// <summary>Executes CORS contracts through a caller-supplied <see cref="HttpClient"/>.</summary>
public sealed class CorsVerifier
{
    private readonly HttpClient _client;

    /// <summary>Creates a verifier that uses the supplied client's configured handler and base address.</summary>
    public CorsVerifier(HttpClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    /// <summary>Executes one contract and evaluates browser-relevant response headers.</summary>
    public async Task<CorsVerificationResult> VerifyAsync(CorsContract contract, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(contract);

        var localIssues = CorsHeaderEvaluator.ValidateContract(contract);
        if (localIssues.Count != 0)
        {
            return new CorsVerificationResult(contract, false, false, null, false, null, localIssues);
        }

        var scenario = contract.Scenario;
        var expectation = contract.Expectation;
        var issues = new List<CorsIssue>();
        var preflightSent = false;
        HttpStatusCode? preflightStatusCode = null;
        var actualRequestSent = false;
        HttpStatusCode? actualStatusCode = null;

        if (scenario.RequiresPreflight)
        {
            preflightSent = true;
            using var preflight = CreatePreflightRequest(scenario);
            HttpResponseMessage? response = null;
            try
            {
                response = await _client.SendAsync(preflight, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                preflightStatusCode = response.StatusCode;
                var preflightEvaluation = CorsHeaderEvaluator.EvaluatePreflight(response, scenario, expectation);
                issues.AddRange(preflightEvaluation.Issues);

                if (!expectation.IsAllowed)
                {
                    if (!preflightEvaluation.GrantsCorsAccess)
                    {
                        return CreateResult(contract, true, preflightSent, preflightStatusCode, false, null, issues);
                    }

                    issues.Add(new CorsIssue(
                        CorsFailureKind.UnexpectedCorsPermission,
                        $"The preflight response granted {scenario.Method.Method} from the requested origin, but the contract expected access to be denied."));
                    return CreateResult(contract, false, preflightSent, preflightStatusCode, false, null, issues);
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
            finally
            {
                response?.Dispose();
            }
        }

        using var actual = CreateActualRequest(scenario);
        HttpResponseMessage? actualResponse = null;
        try
        {
            actualRequestSent = true;
            actualResponse = await _client.SendAsync(actual, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            actualStatusCode = actualResponse.StatusCode;
            var actualEvaluation = CorsHeaderEvaluator.EvaluateActual(actualResponse, scenario, expectation);
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
        finally
        {
            actualResponse?.Dispose();
        }

        return CreateResult(contract, false, preflightSent, preflightStatusCode, actualRequestSent, actualStatusCode, issues);
    }

    /// <summary>Executes the contracts in a bounded matrix in declaration order.</summary>
    public async Task<IReadOnlyList<CorsVerificationResult>> VerifyMatrixAsync(CorsMatrix matrix, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(matrix);
        var results = new List<CorsVerificationResult>(matrix.Contracts.Count);
        foreach (var contract in matrix.Contracts)
        {
            results.Add(await VerifyAsync(contract, cancellationToken).ConfigureAwait(false));
        }

        return results.AsReadOnly();
    }

    internal static HttpRequestMessage CreatePreflightRequest(CorsScenario scenario)
    {
        var request = new HttpRequestMessage(HttpMethod.Options, scenario.Path);
        AddOrigin(request, scenario.Origin);
        request.Headers.TryAddWithoutValidation("Access-Control-Request-Method", scenario.Method.Method);
        if (scenario.RequestedHeaders.Count != 0)
        {
            request.Headers.TryAddWithoutValidation(
                "Access-Control-Request-Headers",
                string.Join(", ", scenario.RequestedHeaders.Select(static header => header.ToLowerInvariant())));
        }

        return request;
    }

    internal static HttpRequestMessage CreateActualRequest(CorsScenario scenario)
    {
        var request = new HttpRequestMessage(scenario.Method, scenario.Path);
        AddOrigin(request, scenario.Origin);
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

    public static IReadOnlyList<CorsIssue> ValidateContract(CorsContract contract)
    {
        var issues = new List<CorsIssue>();
        if (contract.Scenario.UseCredentials && contract.Expectation.AllowWildcardOrigin)
        {
            issues.Add(new CorsIssue(
                CorsFailureKind.MalformedScenario,
                "A credentialed scenario cannot require a wildcard allow-origin response."));
        }

        if (!contract.Scenario.RequiresPreflight && contract.Expectation.ExpectedMaxAge is not null)
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

    public static CorsEvaluation EvaluatePreflight(HttpResponseMessage response, CorsScenario scenario, CorsExpectation expectation)
    {
        var issues = new List<CorsIssue>();
        var originPermission = EvaluateOrigin(response, scenario, expectation, issues);
        var methodPermission = HasToken(response, AllowMethods, scenario.Method.Method, !scenario.UseCredentials);
        var headersPermission = scenario.RequestedHeaders.All(header => HasToken(response, AllowHeaders, header, !scenario.UseCredentials));
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

        var value = values[0].Trim();
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
            var vary = GetValues(response, "Vary").SelectMany(static value => value.Split(','));
            if (!vary.Any(static value => value.Trim().Equals("Origin", StringComparison.OrdinalIgnoreCase)))
            {
                issues.Add(new CorsIssue(CorsFailureKind.MissingVaryOrigin, "The exact-origin response did not include Vary: Origin."));
            }
        }
    }

    private static void EvaluateExposedHeaders(HttpResponseMessage response, CorsScenario scenario, CorsExpectation expectation, List<CorsIssue> issues)
    {
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
               values[0].Equals("true", StringComparison.Ordinal) &&
               !IsWildcardOrigin(response);
    }

    private static bool IsWildcardOrigin(HttpResponseMessage response) =>
        GetValues(response, AllowOrigin).Count == 1 && GetValues(response, AllowOrigin)[0].Trim() == "*";

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

    private static bool IsCorsSafelistedResponseHeader(string expected) =>
        CorsSafelistedResponseHeaders.Any(header => header.Equals(expected, StringComparison.OrdinalIgnoreCase));

    private static bool TryParseCorsList(HttpResponseMessage response, string headerName, out IReadOnlyList<string> tokens)
    {
        var values = GetValues(response, headerName);
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

                var member = TrimOws(value[memberStart..index]);
                if (!IsHttpToken(member))
                {
                    tokens = Array.Empty<string>();
                    return false;
                }

                parsed.Add(member);
                memberStart = index + 1;
            }
        }

        if (parsed.Count == 0 || parsed.Count > 1 && parsed.Contains("*", StringComparer.Ordinal))
        {
            tokens = Array.Empty<string>();
            return false;
        }

        tokens = parsed;
        return true;
    }

    private static bool TryParseDeltaSeconds(string value, out long seconds)
    {
        value = TrimOws(value);
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

    private static string TrimOws(string value)
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
