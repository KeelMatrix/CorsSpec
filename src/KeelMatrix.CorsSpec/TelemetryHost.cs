using KeelMatrix.Telemetry;

namespace KeelMatrix.CorsSpec;

internal interface ICorsTelemetry
{
    void TrackActivation();
}

internal sealed class SharedTelemetry : ICorsTelemetry
{
    private readonly Client _client;

    public SharedTelemetry()
        : this("CorsSpec")
    {
    }

    internal SharedTelemetry(string toolName)
    {
        _client = new Client(toolName, typeof(SharedTelemetry));
    }

    public void TrackActivation() => _client.TrackActivation();
}

internal static class TelemetryHost
{
    public static ICorsTelemetry Create() => new SharedTelemetry();

    public static bool IsSuppressed() => IsSuppressed(Environment.GetEnvironmentVariable);

    internal static bool IsSuppressed(Func<string, string?> getEnvironmentVariable) =>
        string.Equals(getEnvironmentVariable("KEELMATRIX_NO_TELEMETRY"), "1", StringComparison.Ordinal);

    public static void TrackActivation(ICorsTelemetry telemetry)
    {
        try
        {
            telemetry.TrackActivation();
        }
        catch
        {
            // Telemetry is best-effort and must never affect verification.
        }
    }
}
