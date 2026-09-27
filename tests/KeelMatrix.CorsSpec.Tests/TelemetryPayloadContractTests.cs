using System.Text.Json;

namespace KeelMatrix.CorsSpec.Tests;

public sealed class TelemetryPayloadContractTests
{
    private static readonly string[] ExpectedActivationFields =
    [
        "ci",
        "event",
        "installation_hash",
        "os",
        "project_hash",
        "runtime",
        "schema_version",
        "telemetry_version",
        "timestamp",
        "tool",
        "tool_version"
    ];

    private static readonly string[] ProhibitedContractFieldFragments =
    [
        "authorization",
        "body",
        "cookie",
        "credential",
        "diagnostic",
        "endpoint",
        "exception",
        "header",
        "host",
        "method",
        "origin",
        "path",
        "request",
        "response",
        "token"
    ];

    [Fact]
    public void Captured_activation_payload_matches_shared_allowlist_and_excludes_contract_data()
    {
        // Captured from the KeelMatrix.Telemetry 0.1.1 activation contract.
        const string capturedPayload = """
            {
              "event": "activation",
              "tool": "corsspec",
              "tool_version": "0.1.0",
              "telemetry_version": "0.1.1",
              "schema_version": 1,
              "project_hash": "project-hash",
              "installation_hash": "installation-hash",
              "runtime": "net8.0",
              "os": "windows",
              "ci": false,
              "timestamp": "2026-09-27T00:00:00Z"
            }
            """;

        using var document = JsonDocument.Parse(capturedPayload);
        var root = document.RootElement;

        Assert.Equal(JsonValueKind.Object, root.ValueKind);
        Assert.Equal(
            ExpectedActivationFields,
            root.EnumerateObject().Select(property => property.Name).OrderBy(name => name).ToArray());
        Assert.Equal("activation", root.GetProperty("event").GetString());
        Assert.Equal(JsonValueKind.Number, root.GetProperty("schema_version").ValueKind);
        Assert.True(root.GetProperty("ci").ValueKind is JsonValueKind.True or JsonValueKind.False);
        Assert.False(root.TryGetProperty("week", out _));

        foreach (var property in root.EnumerateObject())
        {
            foreach (var fragment in ProhibitedContractFieldFragments)
            {
                Assert.DoesNotContain(fragment, property.Name, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
