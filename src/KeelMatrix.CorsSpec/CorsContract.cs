namespace KeelMatrix.CorsSpec;

/// <summary>Pairs one CORS request scenario with its observable response expectation.</summary>
public sealed class CorsContract
{
    /// <summary>Creates a CORS contract.</summary>
    public CorsContract(CorsScenario scenario, CorsExpectation expectation)
    {
        Scenario = scenario ?? throw new ArgumentNullException(nameof(scenario));
        Expectation = expectation ?? throw new ArgumentNullException(nameof(expectation));
    }

    /// <summary>Gets the request scenario.</summary>
    public CorsScenario Scenario { get; }

    /// <summary>Gets the response expectation.</summary>
    public CorsExpectation Expectation { get; }
}
