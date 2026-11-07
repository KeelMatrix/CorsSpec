namespace KeelMatrix.CorsSpec.IntegrationTests;

public sealed class ClientBoundaryTests
{
    [Fact]
    public Task Shared_clients_and_ordinary_client_rejections_match_the_package_contract() => ClientBoundaryProbes.RunAsync();
}
