namespace KeelMatrix.CorsSpec.IntegrationTests;

public sealed class FirstResponseIntegrityTests
{
    [Fact]
    public Task First_response_values_survive_in_place_mutation_and_cleanup() => FirstResponseProbes.RunAsync();
}
