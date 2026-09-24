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

        return results;
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
        var methodPermission = HasToken(response, AllowMethods, scenario.Method.Method);
        var headersPermission = scenario.RequestedHeaders.All(header => HasToken(response, AllowHeaders, header));

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
            return new CorsEvaluation(originPermission && methodPermission && headersPermission && HasCredentialPermission(response, scenario), issues.ToArray());
        }

        var grants = originPermission && methodPermission && headersPermission && HasCredentialPermission(response, scenario);
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
            EvaluateExposedHeaders(response, expectation, issues);
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

    private static void EvaluateExposedHeaders(HttpResponseMessage response, CorsExpectation expectation, List<CorsIssue> issues)
    {
        if (expectation.ExpectedExposedHeaders.Count == 0)
        {
            return;
        }

        var exposed = GetValues(response, ExposeHeaders)
            .SelectMany(static value => value.Split(','))
            .Select(static value => value.Trim())
            .Where(static value => value.Length != 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (expectation.ExpectedExposedHeaders.Any(header => !exposed.Contains(header)))
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
        if (values.Count != 1 || !long.TryParse(values[0].Trim(), out var seconds) || seconds != (long)expected.TotalSeconds)
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

        return GetValues(response, AllowCredentials).Count == 1 &&
               GetValues(response, AllowCredentials)[0].Trim().Equals("true", StringComparison.OrdinalIgnoreCase) &&
               !IsWildcardOrigin(response);
    }

    private static bool IsWildcardOrigin(HttpResponseMessage response) =>
        GetValues(response, AllowOrigin).Count == 1 && GetValues(response, AllowOrigin)[0].Trim() == "*";

    private static bool HasToken(HttpResponseMessage response, string headerName, string expected)
    {
        var values = GetValues(response, headerName);
        return values
            .SelectMany(static value => value.Split(','))
            .Select(static value => value.Trim())
            .Any(value => value.Equals(expected, StringComparison.OrdinalIgnoreCase));
    }

    private static List<string> GetValues(HttpResponseMessage response, string headerName)
    {
        if (response.Headers.TryGetValues(headerName, out var values))
        {
            return values.ToList();
        }

        return new List<string>();
    }
}
