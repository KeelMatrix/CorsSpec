using System.Net;

namespace KeelMatrix.CorsSpec.Tests;

public sealed class FirstResponseEvidenceTests
{
    public static IEnumerable<object[]> ProvenanceCases()
    {
        foreach (var provenance in new[] { "missing", "method", "uri", "valid" })
        {
            foreach (var phase in new[] { "simple", "preflight", "actual" })
            {
                foreach (var factory in new[] { false, true })
                {
                    foreach (var allowed in new[] { false, true })
                    {
                        yield return [provenance, phase, factory, allowed];
                    }
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(ProvenanceCases))]
    public async Task Outer_handler_cannot_repair_missing_or_changed_first_response_provenance(string provenance, string phase, bool factory, bool allowed)
    {
        HttpMessageHandler Handler() => new RepairMetadataHandler(new ProvenanceHandler(provenance, phase));
        using var client = new TestHttpClient(Handler()) { BaseAddress = new Uri("https://service.test") };
        var verifier = factory ? new CorsVerifier(client, Handler) : new CorsVerifier(client);
        var result = await verifier.VerifyAsync(new CorsContract(
            new CorsScenario("/orders", "https://app.example", phase == "simple" ? HttpMethod.Get : HttpMethod.Delete),
            allowed ? CorsExpectation.Allowed() : CorsExpectation.Denied()));

        if (provenance == "valid")
        {
            Assert.Equal(allowed, result.IsSuccess);
            Assert.DoesNotContain(result.Issues, issue => issue.Kind == CorsFailureKind.RedirectNotSupported);
        }
        else
        {
            Assert.False(result.IsSuccess);
            Assert.Contains(result.Issues, issue => issue.Kind == CorsFailureKind.RedirectNotSupported);
        }
    }

    private sealed class ProvenanceHandler(string provenance, string phase) : HttpMessageHandler
    {
        private readonly List<HttpRequestMessage> _metadata = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = ResponseFactory.Cors(statusCode: HttpStatusCode.OK);
            var targeted = (request.Method == HttpMethod.Options) == (phase == "preflight");
            if (targeted && provenance == "missing") { return Task.FromResult(response); }
            var metadata = new HttpRequestMessage(targeted && provenance == "method" ? HttpMethod.Patch : request.Method,
                targeted && provenance == "uri" ? new Uri("https://elsewhere.invalid/other") : request.RequestUri);
            _metadata.Add(metadata);
            response.RequestMessage = metadata;
            return Task.FromResult(response);
        }
        protected override void Dispose(bool disposing)
        {
            foreach (var metadata in _metadata) { metadata.Dispose(); }
            base.Dispose(disposing);
        }
    }

    private sealed class RepairMetadataHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = await base.SendAsync(request, cancellationToken);
            response.RequestMessage = request;
            return response;
        }
    }
}
