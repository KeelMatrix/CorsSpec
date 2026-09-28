using System.Net;
using System.Net.Http;
using KeelMatrix.CorsSpec;

namespace KeelMatrix.CorsSpec.Tests;

public sealed class RequestGenerationTests
{
    [Theory]
    [InlineData("/orders")]
    [InlineData("orders")]
    public void Valid_application_paths_create_relative_requests(string path)
    {
        var scenario = new CorsScenario(path, "https://app.example", HttpMethod.Get);
        using var request = CorsVerifier.CreateActualRequest(scenario);

        Assert.False(request.RequestUri!.IsAbsoluteUri);
    }

    [Fact]
    public async Task Simple_request_sends_origin_and_actual_method_without_preflight()
    {
        var handler = new RecordingHandler(request => ResponseFactory.Cors());
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://service.test") };
        var contract = new CorsContract(
            new CorsScenario("/orders", "https://app.example", HttpMethod.Get),
            CorsExpectation.Allowed());

        var result = await new CorsVerifier(client).VerifyAsync(contract);

        Assert.True(result.IsSuccess, result.Summary);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("/orders", request.RequestUri!.AbsolutePath);
        Assert.Equal("https://app.example", request.Headers.GetValues("Origin").Single());
        Assert.Empty(request.Headers.Accept);
    }

    [Theory]
    [InlineData("PATCH", false, false)]
    [InlineData("PATCH", true, false)]
    [InlineData("PATCH", false, true)]
    [InlineData("PATCH", true, true)]
    [InlineData("GET", true, false)]
    [InlineData("GET", true, true)]
    [InlineData("HEAD", true, false)]
    [InlineData("POST", true, true)]
    public async Task Every_preflight_carries_exactly_the_browser_accept_value(
        string method,
        bool includeRequestedHeader,
        bool useCredentials)
    {
        var handler = new RecordingHandler(request =>
        {
            if (request.Method == HttpMethod.Options)
            {
                var accept = request.Headers.Accept.ToArray();
                if (accept.Length != 1 || accept[0].MediaType != "*/*" || accept[0].Quality is not null)
                {
                    return new HttpResponseMessage(HttpStatusCode.NotAcceptable);
                }
            }

            return ResponseFactory.Cors(
                methods: method,
                headers: includeRequestedHeader ? "X-Trace" : null,
                credentials: useCredentials);
        });
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://service.test") };

        var result = await new CorsVerifier(client, handler.CreateSibling).VerifyAsync(new CorsContract(
            new CorsScenario(
                "/orders",
                "https://app.example",
                new HttpMethod(method),
                includeRequestedHeader ? new[] { "X-Trace" } : null,
                useCredentials),
            CorsExpectation.Allowed()));

        Assert.True(result.IsSuccess, result.Summary);
        var preflight = Assert.Single(handler.Requests, request => request.Method == HttpMethod.Options);
        var accept = Assert.Single(preflight.Headers.Accept);
        Assert.Equal("*/*", accept.MediaType);
        Assert.Null(accept.Quality);
    }

    [Fact]
    public async Task Non_simple_request_sends_preflight_with_normalized_metadata_then_actual_request()
    {
        var handler = new RecordingHandler(request => request.Method == HttpMethod.Options
            ? ResponseFactory.Cors(headers: "X-Trace, Authorization", maxAge: "600")
            : ResponseFactory.Cors(exposed: "X-Request-Id"));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://service.test") };
        var contract = new CorsContract(
            new CorsScenario("/orders", "https://app.example", HttpMethod.Delete, new[] { "X-Trace", "Authorization" }),
            CorsExpectation.Allowed(expectedExposedHeaders: new[] { "X-Request-Id" }, expectedMaxAge: TimeSpan.FromMinutes(10)));

        var result = await new CorsVerifier(client, handler.CreateSibling).VerifyAsync(contract);

        Assert.True(result.IsSuccess, result.Summary);
        Assert.Equal(2, handler.Requests.Count);
        var preflight = handler.Requests[0];
        Assert.Equal(HttpMethod.Options, preflight.Method);
        Assert.Equal("https://app.example", preflight.Headers.GetValues("Origin").Single());
        Assert.Equal("DELETE", preflight.Headers.GetValues("Access-Control-Request-Method").Single());
        Assert.Equal("authorization, x-trace", preflight.Headers.GetValues("Access-Control-Request-Headers").Single());
        Assert.Equal(HttpMethod.Delete, handler.Requests[1].Method);
    }

    [Fact]
    public async Task Dedicated_preflight_handler_isolated_from_client_defaults_while_actual_keeps_them()
    {
        var handler = new RecordingHandler(_ => ResponseFactory.Cors(headers: "Authorization, X-Trace", credentials: true));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://service.test") };
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Trace", "caller-default");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", "Bearer synthetic");

        var scenario = new CorsScenario(
            "/orders",
            "https://app.example",
            HttpMethod.Delete,
            new[] { "Accept", "Authorization", "X-Trace" },
            useCredentials: true);

        var result = await new CorsVerifier(client, handler.CreateSibling).VerifyAsync(new CorsContract(scenario, CorsExpectation.Allowed()));

        Assert.True(result.IsSuccess, result.Summary);
        var preflight = Assert.Single(handler.Requests, request => request.Method == HttpMethod.Options);
        Assert.Equal(
            new[] { "Accept", "Access-Control-Request-Headers", "Access-Control-Request-Method", "Origin" },
            preflight.Headers.Select(header => header.Key).OrderBy(key => key, StringComparer.OrdinalIgnoreCase));
        Assert.DoesNotContain("Authorization", preflight.Headers.Select(header => header.Key), StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("X-Trace", preflight.Headers.Select(header => header.Key), StringComparer.OrdinalIgnoreCase);

        var actual = Assert.Single(handler.Requests, request => request.Method == HttpMethod.Delete);
        Assert.Equal("Bearer synthetic", actual.Headers.GetValues("Authorization").Single());
        Assert.Equal("caller-default", actual.Headers.GetValues("X-Trace").Single());
    }

    [Fact]
    public async Task Dedicated_preflight_handler_isolated_from_delegating_handler_headers_while_actual_keeps_them()
    {
        var handler = new RecordingHandler(_ => ResponseFactory.Cors(headers: "Authorization, X-Trace", credentials: true));
        using var client = new HttpClient(new HeaderAddingHandler(handler,
            ("Authorization", "Bearer handler"),
            ("X-Trace", "handler"),
            ("Cookie", "session=handler")))
        {
            BaseAddress = new Uri("https://service.test")
        };

        var scenario = new CorsScenario(
            "/orders",
            "https://app.example",
            HttpMethod.Delete,
            new[] { "Authorization", "X-Trace" },
            useCredentials: true);

        var result = await new CorsVerifier(client, handler.CreateSibling).VerifyAsync(new CorsContract(scenario, CorsExpectation.Allowed()));

        Assert.True(result.IsSuccess, result.Summary);
        var preflight = Assert.Single(handler.Requests, request => request.Method == HttpMethod.Options);
        Assert.Equal(
            new[] { "Accept", "Access-Control-Request-Headers", "Access-Control-Request-Method", "Origin" },
            preflight.Headers.Select(header => header.Key).OrderBy(key => key, StringComparer.OrdinalIgnoreCase));
        Assert.DoesNotContain("Authorization", preflight.Headers.Select(header => header.Key), StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("Cookie", preflight.Headers.Select(header => header.Key), StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("X-Trace", preflight.Headers.Select(header => header.Key), StringComparer.OrdinalIgnoreCase);

        var actual = Assert.Single(handler.Requests, request => request.Method == HttpMethod.Delete);
        Assert.Equal("Bearer handler", actual.Headers.GetValues("Authorization").Single());
        Assert.Equal("handler", actual.Headers.GetValues("X-Trace").Single());
        Assert.Equal("session=handler", actual.Headers.GetValues("Cookie").Single());
    }

    [Theory]
    [InlineData("PATCH", null)]
    [InlineData("GET", "X-Trace")]
    public async Task Factory_preflight_timeout_is_reported_and_does_not_send_actual_request(string method, string? requestedHeader)
    {
        var actualHandler = new RecordingHandler(_ => throw new InvalidOperationException("actual request should not execute"));
        var preflightHandler = new BlockingHandler();
        using var client = new HttpClient(actualHandler)
        {
            BaseAddress = new Uri("https://service.test"),
            Timeout = TimeSpan.FromMilliseconds(100)
        };
        var scenario = new CorsScenario(
            "/orders",
            "https://app.example",
            new HttpMethod(method),
            requestedHeader is null ? null : new[] { requestedHeader });

        var operation = new CorsVerifier(client, () => preflightHandler).VerifyAsync(new CorsContract(
            scenario,
            CorsExpectation.Allowed()));
        var result = await operation.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Kind == CorsFailureKind.NetworkFailure);
        Assert.True(result.PreflightSent);
        Assert.False(result.ActualRequestSent);
        Assert.Equal(1, preflightHandler.RequestCount);
        Assert.True(preflightHandler.IsDisposed);
        Assert.Empty(actualHandler.Requests);
    }

    [Fact]
    public async Task Factory_preflight_explicit_cancellation_remains_prompt_and_disposes_handler()
    {
        var actualHandler = new RecordingHandler(_ => throw new InvalidOperationException("actual request should not execute"));
        var preflightHandler = new BlockingHandler();
        using var client = new HttpClient(actualHandler)
        {
            BaseAddress = new Uri("https://service.test"),
            Timeout = Timeout.InfiniteTimeSpan
        };
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var contract = new CorsContract(
            new CorsScenario("/orders", "https://app.example", HttpMethod.Patch),
            CorsExpectation.Allowed());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new CorsVerifier(client, () => preflightHandler).VerifyAsync(contract, cancellation.Token));

        Assert.True(preflightHandler.IsDisposed);
        Assert.Empty(actualHandler.Requests);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Factory_preflight_timeout_does_not_stall_later_matrix_cells(int hangingCell)
    {
        var actualHandler = new RecordingHandler(_ => ResponseFactory.Cors());
        var factoryDisposals = new List<Func<bool>>();
        var factoryCall = 0;
        using var client = new HttpClient(actualHandler)
        {
            BaseAddress = new Uri("https://service.test"),
            Timeout = TimeSpan.FromMilliseconds(100)
        };
        var contracts = Enumerable.Range(0, 3)
            .Select(_ => new CorsContract(
                new CorsScenario("/orders", "https://app.example", HttpMethod.Patch),
                CorsExpectation.Allowed()))
            .ToArray();
        var matrix = new CorsMatrix(contracts);

        var operation = new CorsVerifier(client, () =>
        {
            if (factoryCall++ == hangingCell)
            {
                var blockingHandler = new BlockingHandler();
                factoryDisposals.Add(() => blockingHandler.IsDisposed);
                return blockingHandler;
            }

            var recordingHandler = new RecordingHandler(_ => ResponseFactory.Cors(methods: "PATCH"));
            factoryDisposals.Add(() => recordingHandler.IsDisposed);
            return recordingHandler;
        }).VerifyMatrixAsync(matrix);
        var results = await operation.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(3, results.Count);
        Assert.Equal(CorsFailureKind.NetworkFailure, Assert.Single(results[hangingCell].Issues).Kind);
        Assert.False(results[hangingCell].ActualRequestSent);
        Assert.All(results.Where((_, index) => index != hangingCell), result => Assert.True(result.IsSuccess, result.Summary));
        Assert.All(factoryDisposals, isDisposed => Assert.True(isDisposed()));
    }

    [Fact]
    public async Task Factory_preflight_fault_is_network_failure_and_disposes_handler()
    {
        var actualHandler = new RecordingHandler(_ => throw new InvalidOperationException("actual request should not execute"));
        var preflightHandler = new FaultingHandler();
        using var client = new HttpClient(actualHandler) { BaseAddress = new Uri("https://service.test") };
        var contract = new CorsContract(
            new CorsScenario("/orders", "https://app.example", HttpMethod.Patch),
            CorsExpectation.Allowed());

        var result = await new CorsVerifier(client, () => preflightHandler).VerifyAsync(contract);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Kind == CorsFailureKind.NetworkFailure);
        Assert.True(preflightHandler.IsDisposed);
        Assert.Empty(actualHandler.Requests);
    }

    [Fact]
    public async Task Undeclared_default_headers_fail_closed_before_any_request()
    {
        var handler = new RecordingHandler(_ => throw new InvalidOperationException("request should not execute"));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://service.test") };
        client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", "Bearer synthetic");

        var contract = new CorsContract(
            new CorsScenario("/orders", "https://app.example", HttpMethod.Delete),
            CorsExpectation.Allowed());

        var result = await new CorsVerifier(client).VerifyAsync(contract);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Kind == CorsFailureKind.MalformedScenario);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Denied_preflight_does_not_send_actual_request()
    {
        var handler = new RecordingHandler(_ => ResponseFactory.Cors(methods: "GET"));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://service.test") };
        var contract = new CorsContract(
            new CorsScenario("/orders", "https://untrusted.example", HttpMethod.Delete),
            CorsExpectation.Denied());

        var result = await new CorsVerifier(client, handler.CreateSibling).VerifyAsync(contract);

        Assert.True(result.IsSuccess, result.Summary);
        Assert.True(result.PreflightSent);
        Assert.False(result.ActualRequestSent);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Origin_is_metadata_only_and_is_never_used_as_destination()
    {
        var handler = new RecordingHandler(_ => ResponseFactory.Cors(origin: "https://does-not-exist.invalid"));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://service.test") };
        var contract = new CorsContract(
            new CorsScenario("/orders", "https://does-not-exist.invalid", HttpMethod.Get),
            CorsExpectation.Allowed());

        var result = await new CorsVerifier(client).VerifyAsync(contract);

        Assert.True(result.IsSuccess, result.Summary);
        Assert.Equal("https://service.test", handler.Requests.Single().RequestUri!.GetLeftPart(UriPartial.Authority));
    }

    [Theory]
    [InlineData("Accept", "text/html")]
    [InlineData("Accept", "text/html?")]
    [InlineData("Accept-Language", "en-US, en;q=0.9")]
    [InlineData("Accept-Language", "en_US")]
    [InlineData("Content-Language", "en-US")]
    [InlineData("Content-Language", "en_US")]
    [InlineData("Content-Type", "text/plain")]
    [InlineData("Content-Type", "application/json")]
    [InlineData("Range", "bytes=0-99")]
    public async Task Caller_header_values_determine_safelisted_request_classification(string header, string value)
    {
        var handler = new RecordingHandler(request => request.Method == HttpMethod.Options
            ? ResponseFactory.Cors(methods: "GET", headers: header)
            : ResponseFactory.Cors(vary: null));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://service.test") };
        var result = await new CorsVerifier(client, handler.CreateSibling).VerifyAsync(new CorsContract(
            new CorsScenario(
                "/orders",
                "https://app.example",
                HttpMethod.Get,
                new[] { header },
                requestHeaders: new[] { new CorsRequestHeader(header, value) }),
            CorsExpectation.Allowed()));

        var safe = header switch
        {
            "Accept" => value == "text/html",
            "Accept-Language" => value == "en-US, en;q=0.9",
            "Content-Language" => value == "en-US",
            "Content-Type" => value == "text/plain",
            "Range" => value == "bytes=0-99",
            _ => false
        };
        Assert.True(result.IsSuccess, result.Summary);
        Assert.Equal(!safe, result.PreflightSent);
        Assert.Equal(safe ? 1 : 2, handler.Requests.Count);
        var actual = Assert.Single(handler.Requests, request => request.Method == HttpMethod.Get);
        var serializedValue = header.Equals("Content-Type", StringComparison.OrdinalIgnoreCase) ||
            header.Equals("Content-Language", StringComparison.OrdinalIgnoreCase)
            ? string.Join(", ", actual.Content?.Headers.GetValues(header) ?? Array.Empty<string>())
            : string.Join(", ", actual.Headers.GetValues(header));
        var expectedSerializedValue = value == "en-US, en;q=0.9" ? "en-US, en; q=0.9" : value;
        Assert.Equal(expectedSerializedValue, serializedValue);
    }
}
