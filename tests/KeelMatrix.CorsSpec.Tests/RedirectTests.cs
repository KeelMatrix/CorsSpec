using System.Net;
using System.Net.Http;
using KeelMatrix.CorsSpec;

namespace KeelMatrix.CorsSpec.Tests;

public sealed class RedirectTests
{
    [Theory]
    [InlineData(301, "/same-target")]
    [InlineData(302, "/changed-method")]
    [InlineData(303, "https://other.test/orders")]
    [InlineData(307, "/same-target")]
    [InlineData(308, "https://other.test/orders")]
    public async Task Actual_redirect_responses_are_explicitly_unsupported(int statusCode, string location)
    {
        var handler = new RecordingHandler(_ =>
        {
            var response = new HttpResponseMessage((HttpStatusCode)statusCode);
            response.Headers.Location = new Uri(location, UriKind.RelativeOrAbsolute);
            return response;
        });
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://service.test") };

        var result = await new CorsVerifier(client).VerifyAsync(new CorsContract(
            new CorsScenario("/orders", "https://app.example", HttpMethod.Get),
            CorsExpectation.Allowed()));

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Kind == CorsFailureKind.RedirectNotSupported);
        Assert.True(result.ActualRequestSent);
    }

    [Theory]
    [InlineData(301)]
    [InlineData(302)]
    [InlineData(303)]
    [InlineData(307)]
    [InlineData(308)]
    public async Task Preflight_redirects_are_explicitly_unsupported(int statusCode)
    {
        var handler = new RecordingHandler(request =>
        {
            if (request.Method == HttpMethod.Options)
            {
                var response = new HttpResponseMessage((HttpStatusCode)statusCode);
                response.Headers.Location = new Uri("https://other.test/orders");
                return response;
            }

            throw new InvalidOperationException("actual request must not run");
        });
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://service.test") };

        var result = await new CorsVerifier(client, handler.CreateSibling).VerifyAsync(new CorsContract(
            new CorsScenario("/orders", "https://app.example", HttpMethod.Delete),
            CorsExpectation.Allowed()));

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Kind == CorsFailureKind.RedirectNotSupported);
        Assert.False(result.ActualRequestSent);
    }

    [Fact]
    public async Task A_final_response_with_changed_request_uri_is_not_accepted_as_the_original_operation()
    {
        var handler = new RecordingHandler(_ =>
        {
            var response = ResponseFactory.Cors(vary: null);
            response.RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://other.test/orders");
            return response;
        });
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://service.test") };

        var result = await new CorsVerifier(client).VerifyAsync(new CorsContract(
            new CorsScenario("/orders", "https://app.example", HttpMethod.Get),
            CorsExpectation.Allowed()));

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Kind == CorsFailureKind.RedirectNotSupported);
    }

    [Fact]
    public async Task One_argument_preflight_redirects_are_guarded_before_response_evaluation()
    {
        var handler = new RecordingHandler(request =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Found);
            response.Headers.Location = new Uri("/redirect", UriKind.Relative);
            return response;
        });
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://service.test") };

        var result = await new CorsVerifier(client).VerifyAsync(new CorsContract(
            new CorsScenario("/orders", "https://app.example", HttpMethod.Delete),
            CorsExpectation.Allowed()));

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Kind == CorsFailureKind.RedirectNotSupported);
        Assert.True(result.PreflightSent);
        Assert.False(result.ActualRequestSent);
    }

    [Fact]
    public async Task Mutated_request_uri_and_method_are_compared_with_the_pre_send_identity()
    {
        using var client = new HttpClient(new MutatingRequestHandler()) { BaseAddress = new Uri("https://service.test") };

        var result = await new CorsVerifier(client).VerifyAsync(new CorsContract(
            new CorsScenario("/orders", "https://app.example", HttpMethod.Get),
            CorsExpectation.Allowed()));

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Kind == CorsFailureKind.RedirectNotSupported);
    }

    [Fact]
    public async Task Factory_redirect_rejection_disposes_the_response_before_returning_failure()
    {
        var content = new TrackingContent();
        var handler = new ResponseHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Found) { Content = content };
            response.Headers.Location = new Uri("https://other.test/orders");
            return response;
        });
        using var client = new HttpClient(new RecordingHandler(_ => ResponseFactory.Cors()))
        {
            BaseAddress = new Uri("https://service.test")
        };

        var result = await new CorsVerifier(client, () => handler).VerifyAsync(new CorsContract(
            new CorsScenario("/orders", "https://app.example", HttpMethod.Delete),
            CorsExpectation.Allowed()));

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Kind == CorsFailureKind.RedirectNotSupported);
        Assert.True(content.IsDisposed);
        Assert.True(handler.IsDisposed);
    }

    [Fact]
    public async Task Non_redirect_304_response_is_not_classified_as_a_redirect()
    {
        var handler = new RecordingHandler(_ => ResponseFactory.Cors(statusCode: HttpStatusCode.NotModified, vary: null));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://service.test") };

        var result = await new CorsVerifier(client).VerifyAsync(new CorsContract(
            new CorsScenario("/orders", "https://app.example", HttpMethod.Get),
            CorsExpectation.Allowed()));

        Assert.True(result.IsSuccess, result.Summary);
        Assert.Equal(HttpStatusCode.NotModified, result.ActualStatusCode);
        Assert.DoesNotContain(result.Issues, issue => issue.Kind == CorsFailureKind.RedirectNotSupported);
    }

    [Fact]
    public async Task Opaque_transport_provenance_fails_closed()
    {
        using var client = new HttpClient(new ResponseHandler(_ => new HttpResponseMessage(HttpStatusCode.OK), assignRequest: false))
        {
            BaseAddress = new Uri("https://service.test")
        };

        var result = await new CorsVerifier(client).VerifyAsync(new CorsContract(
            new CorsScenario("/orders", "https://app.example", HttpMethod.Get),
            CorsExpectation.Denied()));

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Kind == CorsFailureKind.RedirectNotSupported);
    }

    private sealed class MutatingRequestHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.Method = HttpMethod.Post;
            request.RequestUri = new Uri("https://other.test/changed");
            var response = ResponseFactory.Cors(vary: null);
            response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }

    private sealed class ResponseHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _factory;
        private readonly bool _assignRequest;

        public ResponseHandler(Func<HttpRequestMessage, HttpResponseMessage> factory, bool assignRequest = true)
        {
            _factory = factory;
            _assignRequest = assignRequest;
        }

        public bool IsDisposed { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = _factory(request);
            if (_assignRequest)
            {
                response.RequestMessage ??= request;
            }
            return Task.FromResult(response);
        }

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }

    private sealed class TrackingContent : HttpContent
    {
        public bool IsDisposed { get; private set; }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => Task.CompletedTask;

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return true;
        }

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }
}
