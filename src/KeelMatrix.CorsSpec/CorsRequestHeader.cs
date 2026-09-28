namespace KeelMatrix.CorsSpec;

/// <summary>Describes a caller header value that the verifier will serialize on the actual request.</summary>
public sealed class CorsRequestHeader
{
    /// <summary>Creates a request header with a browser-visible value.</summary>
    public CorsRequestHeader(string name, string value)
    {
        Name = Validation.RequireHeaderName(name, nameof(name));
        Value = Validation.RequireHeaderValue(
            value,
            nameof(value),
            allowHorizontalTab: Name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase) ||
                Name.Equals("Range", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Gets the header name.</summary>
    public string Name { get; }

    /// <summary>Gets the header value.</summary>
    public string Value { get; }
}
