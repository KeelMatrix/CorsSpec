namespace KeelMatrix.CorsSpec;

/// <summary>Defines whether a CORS scenario should be granted and which headers must be observable.</summary>
public sealed class CorsExpectation
{
    private CorsExpectation(
        bool isAllowed,
        bool allowWildcardOrigin,
        IReadOnlyList<string> expectedExposedHeaders,
        TimeSpan? expectedMaxAge,
        bool requireVaryOrigin)
    {
        IsAllowed = isAllowed;
        AllowWildcardOrigin = allowWildcardOrigin;
        ExpectedExposedHeaders = expectedExposedHeaders;
        ExpectedMaxAge = expectedMaxAge;
        RequireVaryOrigin = requireVaryOrigin;
    }

    /// <summary>Creates an expectation that the browser contract grants the requested origin.</summary>
    /// <param name="allowWildcardOrigin">Allows `Access-Control-Allow-Origin: *` for a non-credentialed scenario.</param>
    /// <param name="expectedExposedHeaders">Response headers that must be exposed to browser script.</param>
    /// <param name="expectedMaxAge">Preflight cache duration that must be returned, when a preflight is used.</param>
    /// <param name="requireVaryOrigin">Requires `Vary: Origin` when an exact origin is returned. Enable this for policies that vary by origin.</param>
    public static CorsExpectation Allowed(
        bool allowWildcardOrigin = false,
        IEnumerable<string>? expectedExposedHeaders = null,
        TimeSpan? expectedMaxAge = null,
        bool requireVaryOrigin = false)
    {
        var exposed = Validation.NormalizeHeaderNames(expectedExposedHeaders, nameof(expectedExposedHeaders));
        if (expectedMaxAge is { } maxAge)
        {
            Validation.ValidateMaxAge(maxAge, nameof(expectedMaxAge));
        }

        return new CorsExpectation(true, allowWildcardOrigin, exposed, expectedMaxAge, requireVaryOrigin);
    }

    /// <summary>Creates an expectation that browser-relevant headers deny the requested scenario.</summary>
    public static CorsExpectation Denied() => new(false, false, Array.Empty<string>(), null, false);

    /// <summary>Gets a value indicating whether the response is expected to grant access.</summary>
    public bool IsAllowed { get; }

    /// <summary>Gets a value indicating whether a wildcard allow-origin is accepted.</summary>
    public bool AllowWildcardOrigin { get; }

    /// <summary>Gets the response header names expected to be exposed.</summary>
    public IReadOnlyList<string> ExpectedExposedHeaders { get; }

    /// <summary>Gets the expected preflight cache duration, when asserted.</summary>
    public TimeSpan? ExpectedMaxAge { get; }

    /// <summary>Gets a value indicating whether an exact-origin response must include `Vary: Origin`.</summary>
    public bool RequireVaryOrigin { get; }
}
