using System.Net;
using System.Net.Sockets;
using System.Text;

namespace KeelMatrix.CorsSpec.IntegrationTests;

public sealed class EffectiveRequestWireTests
{
    public static IEnumerable<object[]> Values()
    {
        foreach (var allowed in new[] { false, true })
        {
            yield return ["Accept", new string('a', 128), false, allowed];
            yield return ["Accept", new string('a', 129), true, allowed];
            yield return ["Accept", "a\tvalue", false, allowed];
            yield return ["Accept-Language", "a\tvalue", true, allowed];
            yield return ["Range", " \tbytes=0-1 \t", false, allowed];
            yield return ["Content-Type", "text/plain;charset=" + new string('a', 109), false, allowed];
            yield return ["Content-Type", "text/plain;charset=" + new string('a', 110), true, allowed];
            yield return ["Content-Encoding", "gzip", true, allowed];
            yield return ["Content-Language", "en-US", false, allowed];
            yield return ["Content-Disposition", "attachment", true, allowed];
            yield return ["Content-Location", "/file", true, allowed];
            yield return ["Content-MD5", "abcd", true, allowed];
            yield return ["Content-Range", "bytes 0-1/2", true, allowed];
            yield return ["Expires", "tomorrow", true, allowed];
            yield return ["Last-Modified", "yesterday", true, allowed];
        }
    }

    [Theory]
    [MemberData(nameof(Values))]
    public async Task Classification_and_raw_wire_use_the_same_normalized_field(string name, string value, bool preflight, bool allowed)
    {
        await using var server = new RawCorsServer(optionsGrant: false, actualGrant: true);
        using var client = server.CreateClient();
        var scenario = new CorsScenario("/orders", "https://app.example", HttpMethod.Get,
            requestHeaders: [new CorsRequestHeader(name, value)]);

        var result = await new CorsVerifier(client).VerifyAsync(new CorsContract(scenario, Expectation(allowed)));

        Assert.Equal(preflight, result.PreflightSent);
        Assert.Equal(preflight ? !allowed : allowed, result.IsSuccess);
        Assert.Equal(preflight ? "OPTIONS" : "GET", server.Requests.Single().Method);
        if (preflight)
        {
            Assert.Equal(name.ToLowerInvariant(), server.Requests[0].Header("Access-Control-Request-Headers"));
        }
        else
        {
            Assert.Equal(value.Trim(' ', '\t'), server.Requests[0].Header(name));
        }
    }

    [Theory]
    [MemberData(nameof(Values))]
    public async Task Granted_options_and_denied_actual_reach_the_actual_wire(string name, string value, bool preflight, bool allowed)
    {
        await using var server = new RawCorsServer(optionsGrant: true, actualGrant: false);
        using var client = server.CreateClient();
        var result = await new CorsVerifier(client).VerifyAsync(new CorsContract(
            new CorsScenario("/orders", "https://app.example", HttpMethod.Get,
                requestHeaders: [new CorsRequestHeader(name, value)]), Expectation(allowed)));

        Assert.Equal(preflight, result.PreflightSent);
        Assert.Equal(!allowed, result.IsSuccess);
        Assert.True(result.ActualRequestSent);
        Assert.Equal(value.Trim(' ', '\t'), server.Requests.Last().Header(name));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Names_only_handler_values_are_captured_before_classification(bool unsafeValue, bool allowed)
    {
        await using var server = new RawCorsServer(optionsGrant: false, actualGrant: true);
        var value = unsafeValue ? "text/html?" : "text/html";
        using var client = CorsTransport.CreateClient(new SetHeaderHandler(server.Handler(), "Accept", value));
        client.BaseAddress = server.BaseAddress;
        var result = await new CorsVerifier(client, server.Handler).VerifyAsync(new CorsContract(
            new CorsScenario("/orders", "https://app.example", HttpMethod.Get, ["Accept"]), Expectation(allowed)));

        Assert.Equal(unsafeValue, result.PreflightSent);
        Assert.Equal(unsafeValue ? !allowed : allowed, result.IsSuccess);
        Assert.Single(server.Requests);
    }

    [Theory]
    [InlineData("Accept")]
    [InlineData("Accept-Language")]
    [InlineData("Content-Language")]
    [InlineData("Content-Type")]
    [InlineData("Range")]
    [InlineData("Authorization")]
    [InlineData("X-Trace")]
    public async Task Unavailable_names_only_values_cannot_establish_either_polarity(string name)
    {
        await using var server = new RawCorsServer(optionsGrant: false, actualGrant: true);
        using var client = server.CreateClient();
        foreach (var allowed in new[] { false, true })
        {
            var result = await new CorsVerifier(client).VerifyAsync(new CorsContract(
                new CorsScenario("/orders", "https://app.example", HttpMethod.Get, [name]), Expectation(allowed)));
            Assert.False(result.IsSuccess);
            Assert.Contains(result.Issues, issue => issue.Kind == CorsFailureKind.UnsupportedRequest);
        }
        Assert.Empty(server.Requests);
    }

    [Theory]
    [InlineData(65001, 64, false)]
    [InlineData(65001, 65, true)]
    [InlineData(28591, 65, false)]
    [InlineData(28591, 128, false)]
    [InlineData(28591, 129, true)]
    public async Task Non_ascii_boundaries_use_the_selected_wire_encoding(int codePage, int count, bool preflight)
    {
        var encoding = Encoding.GetEncoding(codePage);
        var value = new string('é', count);
        await using var server = new RawCorsServer(optionsGrant: true, actualGrant: true);
        using var client = server.CreateClient(encoding);
        foreach (var allowed in new[] { false, true })
        {
            var result = await new CorsVerifier(client).VerifyAsync(new CorsContract(
                new CorsScenario("/orders", "https://app.example", HttpMethod.Get,
                    requestHeaders: [new CorsRequestHeader("Accept", value)]), Expectation(allowed)));
            Assert.Equal(preflight, result.PreflightSent);
            Assert.Equal(allowed, result.IsSuccess);
            Assert.Equal(encoding.GetBytes(value), server.Requests.Last().HeaderBytes("Accept"));
        }
    }

    [Fact]
    public async Task Default_ascii_transport_rejects_non_ascii_before_host_io()
    {
        await using var server = new RawCorsServer(optionsGrant: false, actualGrant: true);
        using var client = server.CreateClient(Encoding.ASCII);
        var result = await new CorsVerifier(client).VerifyAsync(new CorsContract(
            new CorsScenario("/orders", "https://app.example", HttpMethod.Get,
                requestHeaders: [new CorsRequestHeader("Accept", "é")]), CorsExpectation.Denied()));
        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Kind == CorsFailureKind.UnsupportedRequest);
        Assert.Empty(server.Requests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Default_cookies_are_browser_credentials_and_authorization_is_an_author_header(bool allowed)
    {
        await using var server = new RawCorsServer(optionsGrant: true, actualGrant: true);
        using var client = server.CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation("Cookie", "session=synthetic");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", "Bearer synthetic");
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Trace", "trace");
        var result = await new CorsVerifier(client, server.Handler).VerifyAsync(new CorsContract(
            new CorsScenario("/orders", "https://app.example", HttpMethod.Get, ["Authorization", "X-Trace"], useCredentials: true), Expectation(allowed)));

        Assert.Equal(allowed, result.IsSuccess);
        Assert.Equal("authorization,x-trace", server.Requests[0].Header("Access-Control-Request-Headers"));
        Assert.Null(server.Requests[0].Header("Cookie"));
        Assert.Null(server.Requests[0].Header("Authorization"));
        Assert.Equal("session=synthetic", server.Requests[1].Header("Cookie"));
        Assert.Equal("Bearer synthetic", server.Requests[1].Header("Authorization"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Content_header_formatting_is_observed_before_the_128_byte_decision(bool format)
    {
        var value = "text/plain;charset=" + new string('a', 109);
        await using var server = new RawCorsServer(optionsGrant: true, actualGrant: true);
        using var client = CorsTransport.CreateClient(format ? new FormatContentHandler(server.Handler()) : server.Handler());
        client.BaseAddress = server.BaseAddress;
        var result = await new CorsVerifier(client, server.Handler).VerifyAsync(new CorsContract(
            new CorsScenario("/orders", "https://app.example", HttpMethod.Get,
                requestHeaders: [new CorsRequestHeader("Content-Type", value)]), CorsExpectation.Allowed()));

        Assert.True(result.IsSuccess, result.Summary);
        Assert.Equal(format, result.PreflightSent);
        Assert.Equal(format ? value.Replace(";", "; ", StringComparison.Ordinal) : value, server.Requests.Last().Header("Content-Type"));
    }

    [Theory]
    [InlineData(1024)]
    [InlineData(1025)]
    public async Task Combined_fields_at_aggregate_boundaries_are_classified_as_serialized(int length)
    {
        await using var server = new RawCorsServer(optionsGrant: true, actualGrant: true);
        using var client = server.CreateClient();
        // HttpClient combines these values into one field, including the comma and space.
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", [new string('a', 127), new string('b', length - 129)]);
        var result = await new CorsVerifier(client, server.Handler).VerifyAsync(new CorsContract(
            new CorsScenario("/orders", "https://app.example", HttpMethod.Get, ["Accept"]), CorsExpectation.Allowed()));
        Assert.True(result.IsSuccess, result.Summary);
        Assert.True(result.PreflightSent);
        Assert.Equal(length, server.Requests.Last().HeaderBytes("Accept")!.Length);
    }


    public static IEnumerable<object[]> Overrides()
    {
        foreach (var defaultUnsafe in new[] { false, true })
        {
            foreach (var explicitUnsafe in new[] { false, true })
            {
                foreach (var optionsGrant in new[] { false, true })
                {
                    foreach (var actualGrant in new[] { false, true })
                    {
                        foreach (var allowed in new[] { false, true })
                        { yield return [defaultUnsafe, explicitUnsafe, optionsGrant, actualGrant, allowed]; }
                    }
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(Overrides))]
    public async Task Explicit_values_override_defaults_for_both_polarities_and_all_grant_combinations(
        bool defaultUnsafe, bool explicitUnsafe, bool optionsGrant, bool actualGrant, bool allowed)
    {
        await using var server = new RawCorsServer(optionsGrant, actualGrant);
        using var client = server.CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", defaultUnsafe ? "text/html?" : "text/html");
        var value = explicitUnsafe ? "text/html?" : "text/html";
        var result = await new CorsVerifier(client, server.Handler).VerifyAsync(new CorsContract(
            new CorsScenario("/orders", "https://app.example", HttpMethod.Get,
                requestHeaders: [new CorsRequestHeader("Accept", value)]), Expectation(allowed)));
        var grants = (!explicitUnsafe || optionsGrant) && actualGrant;
        Assert.Equal(allowed == grants, result.IsSuccess);
        Assert.Equal(explicitUnsafe, result.PreflightSent);
        if (!explicitUnsafe || optionsGrant)
        { Assert.Equal(value, server.Requests.Last().Header("Accept")); }
    }


    [Fact]
    public async Task Encoding_selection_is_cached_for_the_exact_bytes_sent()
    {
        await using var server = new RawCorsServer(optionsGrant: true, actualGrant: true);
        var handler = (SocketsHttpHandler)server.Handler();
        var calls = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        handler.RequestHeaderEncodingSelector = (name, _) =>
        {
            calls.TryGetValue(name, out var count);
            calls[name] = count + 1;
            // One selection in preparation and one during execution; a third writer
            // invocation would choose different bytes without the controlled selector.
            return count < 2 ? Encoding.Latin1 : Encoding.UTF8;
        };
        using var client = CorsTransport.CreateClient(handler);
        client.BaseAddress = server.BaseAddress;
        var value = new string('é', 65);
        var result = await new CorsVerifier(client).VerifyAsync(new CorsContract(
            new CorsScenario("/orders", "https://app.example", HttpMethod.Get,
                requestHeaders: [new CorsRequestHeader("Accept", value)]), CorsExpectation.Allowed()));
        Assert.True(result.IsSuccess, result.Summary);
        Assert.False(result.PreflightSent);
        Assert.Equal(Encoding.Latin1.GetBytes(value), server.Requests.Single().HeaderBytes("Accept"));
        Assert.Equal(2, calls["Accept"]);
    }


    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Encoding_callback_cannot_change_the_classified_content_representation(bool allowed)
    {
        await using var server = new RawCorsServer(optionsGrant: false, actualGrant: true);
        var handler = (SocketsHttpHandler)server.Handler();
        handler.RequestHeaderEncodingSelector = (_, request) =>
        {
            request.Content!.Headers.ContentType?.ToString();
            return Encoding.UTF8;
        };
        using var client = CorsTransport.CreateClient(handler);
        client.BaseAddress = server.BaseAddress;
        var result = await new CorsVerifier(client).VerifyAsync(new CorsContract(
            new CorsScenario("/orders", "https://app.example", HttpMethod.Get,
                requestHeaders: [new CorsRequestHeader("Content-Type", "text/plain;charset=" + new string('a', 109))]), Expectation(allowed)));
        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Kind == CorsFailureKind.UnsupportedRequest);
        Assert.Empty(server.Requests);
    }

    private static CorsExpectation Expectation(bool allowed) => allowed ? CorsExpectation.Allowed() : CorsExpectation.Denied();

    private sealed class SetHeaderHandler(HttpMessageHandler inner, string name, string value) : DelegatingHandler(inner)
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.Headers.Remove(name);
            request.Headers.TryAddWithoutValidation(name, value);
            return base.SendAsync(request, cancellationToken);
        }
    }

    private sealed class FormatContentHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _ = request.Content!.Headers.ContentType;
            return base.SendAsync(request, cancellationToken);
        }
    }
}

internal sealed class RawCorsServer : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _serve;
    private readonly bool _optionsGrant;
    private readonly bool _actualGrant;
    internal List<RawRequest> Requests { get; } = [];
    internal Uri BaseAddress { get; }

    internal RawCorsServer(bool optionsGrant, bool actualGrant)
    {
        _optionsGrant = optionsGrant;
        _actualGrant = actualGrant;
        _listener.Start();
        BaseAddress = new Uri($"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}");
        _serve = ServeAsync();
    }

    internal HttpMessageHandler Handler() => new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false, UseProxy = false, ConnectTimeout = BaseAddress.IsLoopback ? TimeSpan.FromSeconds(5) : TimeSpan.FromSeconds(1) };

    internal HttpClient CreateClient(Encoding? encoding = null)
    {
        var handler = (SocketsHttpHandler)Handler();
        handler.RequestHeaderEncodingSelector = (_, _) => encoding ?? Encoding.UTF8;
        var client = CorsTransport.CreateClient(handler);
        client.BaseAddress = BaseAddress;
        return client;
    }

    private async Task ServeAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                using var connection = await _listener.AcceptTcpClientAsync(_stop.Token);
                await using var stream = connection.GetStream();
                var data = new List<byte>();
                var next = new byte[1];
                while (await stream.ReadAsync(next, _stop.Token) != 0)
                {
                    data.Add(next[0]);
                    if (data.Count >= 4 && data[^4] == 13 && data[^3] == 10 && data[^2] == 13 && data[^1] == 10) { break; }
                }
                var request = new RawRequest(Encoding.Latin1.GetString(data.ToArray()));
                Requests.Add(request);
                var grant = request.Method == "OPTIONS" ? _optionsGrant : _actualGrant;
                var headers = grant ? "Access-Control-Allow-Origin: https://app.example\r\nAccess-Control-Allow-Credentials: true\r\nAccess-Control-Allow-Methods: GET,DELETE,POST\r\nAccess-Control-Allow-Headers: " + (request.Header("Access-Control-Request-Headers") ?? "*") + "\r\nVary: Origin\r\n" : "";
                var response = Encoding.ASCII.GetBytes("HTTP/1.1 204 No Content\r\n" + headers + "Connection: close\r\n\r\n");
                await stream.WriteAsync(response, _stop.Token);
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        _listener.Stop();
        await _serve;
        _stop.Dispose();
    }
}

internal sealed class RawRequest(string raw)
{
    internal string Method => raw.Split(' ', 2)[0];
    internal string? Header(string name) => raw.Split("\r\n").Skip(1)
        .FirstOrDefault(line => line.StartsWith(name + ":", StringComparison.OrdinalIgnoreCase))?[(name.Length + 1)..].Trim(' ', '\t');
    internal byte[]? HeaderBytes(string name) => Header(name) is { } value ? Encoding.Latin1.GetBytes(value) : null;
}
