namespace KeelMatrix.CorsSpec;

/// <summary>Bounds and groups a small set of CORS contracts for repeated verification.</summary>
public sealed class CorsMatrix
{
    internal const int MaximumContracts = 256;

    /// <summary>Creates a matrix containing one or more contracts.</summary>
    public CorsMatrix(IEnumerable<CorsContract> contracts)
    {
        ArgumentNullException.ThrowIfNull(contracts);
        var values = new List<CorsContract>(MaximumContracts);
        foreach (var contract in contracts)
        {
            if (contract is null)
            {
                throw new ArgumentException("A CORS matrix cannot contain a null contract.", nameof(contracts));
            }

            if (values.Count == MaximumContracts)
            {
                throw new ArgumentException($"A CORS matrix cannot contain more than {MaximumContracts} contracts.", nameof(contracts));
            }

            values.Add(contract);
        }

        if (values.Count == 0)
        {
            throw new ArgumentException("A CORS matrix must contain at least one contract.", nameof(contracts));
        }

        Contracts = values.AsReadOnly();
    }

    /// <summary>Gets the contracts in execution order.</summary>
    public IReadOnlyList<CorsContract> Contracts { get; }
}
