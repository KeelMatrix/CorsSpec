using System.Net;

namespace KeelMatrix.CorsSpec;

/// <summary>Contains the structured outcome of one CORS contract execution.</summary>
public sealed class CorsVerificationResult
{
    internal CorsVerificationResult(
        CorsContract contract,
        bool isSuccess,
        bool preflightSent,
        HttpStatusCode? preflightStatusCode,
        bool actualRequestSent,
        HttpStatusCode? actualStatusCode,
        IReadOnlyList<CorsIssue> issues)
    {
        Contract = contract;
        IsSuccess = isSuccess;
        PreflightSent = preflightSent;
        PreflightStatusCode = preflightStatusCode;
        ActualRequestSent = actualRequestSent;
        ActualStatusCode = actualStatusCode;
        Issues = issues;
    }

    /// <summary>Gets the contract that was evaluated.</summary>
    public CorsContract Contract { get; }

    /// <summary>Gets a value indicating whether the contract passed.</summary>
    public bool IsSuccess { get; }

    /// <summary>Gets a value indicating whether an OPTIONS preflight was sent.</summary>
    public bool PreflightSent { get; }

    /// <summary>Gets the preflight status for diagnostics; it is never the CORS verdict.</summary>
    public HttpStatusCode? PreflightStatusCode { get; }

    /// <summary>Gets a value indicating whether the actual request was sent.</summary>
    public bool ActualRequestSent { get; }

    /// <summary>Gets the actual response status for context; it is never the CORS verdict.</summary>
    public HttpStatusCode? ActualStatusCode { get; }

    /// <summary>Gets the header-level failures, if any.</summary>
    public IReadOnlyList<CorsIssue> Issues { get; }

    /// <summary>Gets a concise human-readable outcome.</summary>
    public string Summary => IsSuccess ? "CORS contract passed." : string.Join(" ", Issues.Select(static issue => issue.Message));

    /// <summary>Throws when the contract did not pass.</summary>
    public void EnsureSuccess()
    {
        if (!IsSuccess)
        {
            throw new InvalidOperationException(Summary);
        }
    }
}
