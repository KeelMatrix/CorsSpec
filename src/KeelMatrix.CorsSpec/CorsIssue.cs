namespace KeelMatrix.CorsSpec;

/// <summary>Describes one header-level CORS verification failure.</summary>
public sealed class CorsIssue
{
    internal CorsIssue(CorsFailureKind kind, string message)
    {
        Kind = kind;
        Message = message;
    }

    /// <summary>Gets the stable failure category.</summary>
    public CorsFailureKind Kind { get; }

    /// <summary>Gets a local diagnostic that does not include response bodies or credentials.</summary>
    public string Message { get; }
}
