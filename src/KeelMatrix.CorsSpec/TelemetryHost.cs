using KeelMatrix.Telemetry;

namespace KeelMatrix.CorsSpec;

internal interface ICorsTelemetry
{
    void TrackActivation();
}

internal sealed class SharedTelemetry : ICorsTelemetry
{
    public void TrackActivation() => new Client("CorsSpec", typeof(SharedTelemetry)).TrackActivation();
}
