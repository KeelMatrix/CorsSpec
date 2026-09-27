using KeelMatrix.Telemetry;

namespace KeelMatrix.CorsSpec;

internal interface ICorsTelemetry
{
    void TrackActivation();
}

internal sealed class SharedTelemetry : ICorsTelemetry
{
    private static readonly Client Client = new("CorsSpec", typeof(SharedTelemetry));

    public void TrackActivation() => Client.TrackActivation();
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
