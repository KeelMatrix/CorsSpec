namespace KeelMatrix.CorsSpec;

/// <summary>Classifies a failed CORS contract dimension.</summary>
public enum CorsFailureKind
{
    /// <summary>The scenario or expectation was contradictory before I/O.</summary>
    MalformedScenario,

    /// <summary>The response did not grant the requested origin.</summary>
    MissingOrMismatchedAllowOrigin,

    /// <summary>The preflight did not grant the requested method.</summary>
    MethodRejected,

    /// <summary>The preflight response did not return a successful HTTP status.</summary>
    PreflightStatusRejected = 10,

    /// <summary>The preflight did not grant one or more requested headers.</summary>
    RequestedHeaderRejected = 3,

    /// <summary>The response did not satisfy the credentialed CORS contract.</summary>
    CredentialsMismatch = 4,

    /// <summary>The response lacked the required Origin variation marker.</summary>
    MissingVaryOrigin = 5,

    /// <summary>The response did not expose the asserted response headers.</summary>
    ExposedHeadersMismatch = 6,

    /// <summary>The preflight did not return the asserted cache duration.</summary>
    MaxAgeMismatch = 7,

    /// <summary>The response granted a scenario that was expected to be denied.</summary>
    UnexpectedCorsPermission = 8,

    /// <summary>The caller's HttpClient could not complete a request.</summary>
    NetworkFailure = 9
}
