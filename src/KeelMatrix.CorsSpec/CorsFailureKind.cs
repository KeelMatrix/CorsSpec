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

    /// <summary>The preflight did not grant one or more requested headers.</summary>
    RequestedHeaderRejected,

    /// <summary>The response did not satisfy the credentialed CORS contract.</summary>
    CredentialsMismatch,

    /// <summary>The response lacked the required Origin variation marker.</summary>
    MissingVaryOrigin,

    /// <summary>The response did not expose the asserted response headers.</summary>
    ExposedHeadersMismatch,

    /// <summary>The preflight did not return the asserted cache duration.</summary>
    MaxAgeMismatch,

    /// <summary>The response granted a scenario that was expected to be denied.</summary>
    UnexpectedCorsPermission,

    /// <summary>The caller's HttpClient could not complete a request.</summary>
    NetworkFailure
}
