using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using KeelMatrix.CorsSpec;

namespace KeelMatrix.CorsSpec.Tests;

public sealed class TelemetryBoundaryTests
{
    private static readonly string[] ActivationFields =
    [
        "event",
        "tool",
        "tool_version",
        "telemetry_version",
        "schema_version",
        "project_hash",
        "installation_hash",
        "runtime",
        "os",
        "ci",
        "timestamp"
    ];

    [Fact]
    public async Task Production_shared_telemetry_serializes_only_the_documented_activation_fields_at_a_local_boundary()
    {
        var oldSuppression = Environment.GetEnvironmentVariable("KEELMATRIX_NO_TELEMETRY");
        var telemetryAssembly = typeof(KeelMatrix.Telemetry.Client).Assembly;
        var telemetryConfig = telemetryAssembly.GetType("KeelMatrix.Telemetry.TelemetryConfig", throwOnError: true)!;
        var setUrlOverride = telemetryConfig.GetMethod("SetUrlOverrideForTests", BindingFlags.Static | BindingFlags.NonPublic)!;
        var resetTelemetry = telemetryConfig.GetMethod("ResetProcessDisabledForTests", BindingFlags.Static | BindingFlags.NonPublic)!;
        var isTelemetryDisabled = telemetryConfig.GetMethod("IsTelemetryDisabled", BindingFlags.Static | BindingFlags.NonPublic)!;
        var testToolName = $"CorsSpecTest-{Guid.NewGuid():N}"[..24];
        var telemetryRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "KeelMatrix",
            testToolName.ToLowerInvariant());

        using var collector = new LocalTelemetryCollector();
        try
        {
            Environment.SetEnvironmentVariable("KEELMATRIX_NO_TELEMETRY", "0");
            resetTelemetry.Invoke(null, null);
            Assert.False((bool)isTelemetryDisabled.Invoke(null, null)!);
            ClearTelemetryWorkers(telemetryAssembly);
            setUrlOverride.Invoke(null, new object?[] { collector.Endpoint });

            var handler = new RecordingHandler(request => request.Method == HttpMethod.Options
                ? ResponseFactory.Cors(origin: "https://internal.example", methods: "DELETE", headers: "Authorization")
                : ResponseFactory.Cors(origin: "https://internal.example"));
            using var client = new HttpClient(handler) { BaseAddress = new Uri("https://service.test") };
            var verification = new CorsVerifier(client, handler.CreateSibling, new SharedTelemetry(testToolName), static () => false).VerifyAsync(new CorsContract(
                new CorsScenario(
                    "/private/orders",
                    "https://internal.example",
                    HttpMethod.Delete,
                    new[] { "Authorization" },
                    requestHeaders: new[] { new CorsRequestHeader("Authorization", "Bearer synthetic-secret") }),
                CorsExpectation.Allowed()));

            var result = await verification.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.True(result.IsSuccess, result.Summary);

            var payload = await collector.Payload.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(HttpStatusCode.InternalServerError, collector.ResponseStatus);
            using var document = JsonDocument.Parse(payload);
            var fields = document.RootElement.EnumerateObject().Select(static property => property.Name).OrderBy(static name => name).ToArray();
            Assert.Equal(ActivationFields.OrderBy(static name => name), fields);
            Assert.Equal("activation", document.RootElement.GetProperty("event").GetString());

            foreach (var forbidden in new[]
            {
                "https://internal.example",
                "/private/orders",
                "DELETE",
                "Authorization",
                "Bearer synthetic-secret",
                "response body",
                "diagnostic"
            })
            {
                Assert.DoesNotContain(forbidden, payload, StringComparison.Ordinal);
            }
        }
        finally
        {
            setUrlOverride.Invoke(null, new object?[] { null });
            ClearTelemetryWorkers(telemetryAssembly);
            resetTelemetry.Invoke(null, null);
            if (Directory.Exists(telemetryRoot))
            {
                TryDeleteTelemetryRoot(telemetryRoot);
            }
            Environment.SetEnvironmentVariable("KEELMATRIX_NO_TELEMETRY", oldSuppression);
        }
    }

    private static void ClearTelemetryWorkers(Assembly telemetryAssembly)
    {
        var registryType = telemetryAssembly.GetType("KeelMatrix.Telemetry.Infrastructure.TelemetryWorkerRegistry", throwOnError: true)!;
        var workersField = registryType.GetField("Workers", BindingFlags.Static | BindingFlags.NonPublic)!;
        var workers = workersField.GetValue(null)!;
        var workerEntries = (System.Collections.IEnumerable)workers;
        foreach (var entry in workerEntries)
        {
            var lazy = entry.GetType().GetProperty("Value")?.GetValue(entry);
            var worker = lazy?.GetType().GetProperty("Value")?.GetValue(lazy);
            worker?.GetType().GetMethod("Dispose", BindingFlags.Instance | BindingFlags.Public)?.Invoke(worker, null);
        }

        workers.GetType().GetMethod("Clear", BindingFlags.Instance | BindingFlags.Public)!.Invoke(workers, null);
    }

    private static void TryDeleteTelemetryRoot(string path)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (IOException) when (attempt < 19)
            {
                Thread.Sleep(50);
            }
            catch (UnauthorizedAccessException) when (attempt < 19)
            {
                Thread.Sleep(50);
            }
            catch (IOException)
            {
                return;
            }
            catch (UnauthorizedAccessException)
            {
                return;
            }
        }
    }

    private sealed class LocalTelemetryCollector : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly TaskCompletionSource<string> _payload = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly CancellationTokenSource _cancellation = new();
        private readonly Task _receiveTask;

        public LocalTelemetryCollector()
        {
            var portProbe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            portProbe.Start();
            var port = ((IPEndPoint)portProbe.LocalEndpoint).Port;
            portProbe.Stop();

            Endpoint = new Uri($"http://127.0.0.1:{port}/");
            _listener.Prefixes.Add(Endpoint.AbsoluteUri);
            _listener.Start();
            _receiveTask = ReceiveAsync();
        }

        public Uri Endpoint { get; }

        public Task<string> Payload => _payload.Task;

        public HttpStatusCode? ResponseStatus { get; private set; }

        private async Task ReceiveAsync()
        {
            try
            {
                var context = await _listener.GetContextAsync().WaitAsync(_cancellation.Token).ConfigureAwait(false);
                using var reader = new StreamReader(context.Request.InputStream);
                var payload = await reader.ReadToEndAsync(_cancellation.Token).ConfigureAwait(false);
                _payload.TrySetResult(payload);
                ResponseStatus = HttpStatusCode.InternalServerError;
                context.Response.StatusCode = (int)ResponseStatus.Value;
                context.Response.Close();
            }
            catch (Exception exception) when (exception is HttpListenerException or ObjectDisposedException or OperationCanceledException)
            {
                _payload.TrySetException(exception);
            }
        }

        public void Dispose()
        {
            _cancellation.Cancel();
            _listener.Stop();
            _listener.Close();
            try
            {
                _receiveTask.GetAwaiter().GetResult();
            }
            catch (Exception) when (_payload.Task.IsCanceled || _payload.Task.IsFaulted)
            {
            }

            _cancellation.Dispose();
        }
    }
}
