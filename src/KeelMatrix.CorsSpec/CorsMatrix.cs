namespace KeelMatrix.CorsSpec;

/// <summary>Bounds and groups a small set of CORS contracts for repeated verification.</summary>
public sealed class CorsMatrix
{
    internal const int MaximumContracts = 256;

    /// <summary>Creates a matrix containing one or more contracts.</summary>
    public CorsMatrix(IEnumerable<CorsContract> contracts)
    {
        ArgumentNullException.ThrowIfNull(contracts);
        var values = contracts.ToArray();
        if (values.Length == 0)
        {
            throw new ArgumentException("A CORS matrix must contain at least one contract.", nameof(contracts));
        }

        if (values.Length > MaximumContracts)
        {
            throw new ArgumentException($"A CORS matrix cannot contain more than {MaximumContracts} contracts.", nameof(contracts));
        }

        if (values.Any(static contract => contract is null))
        {
            throw new ArgumentException("A CORS matrix cannot contain a null contract.", nameof(contracts));
        }

        Contracts = values;
    }

    /// <summary>Gets the contracts in execution order.</summary>
    public IReadOnlyList<CorsContract> Contracts { get; }
}
