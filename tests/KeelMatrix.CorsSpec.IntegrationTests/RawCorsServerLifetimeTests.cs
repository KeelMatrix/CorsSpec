using System.Net.Sockets;
using System.Text;

namespace KeelMatrix.CorsSpec.IntegrationTests;

public sealed class RawCorsServerLifetimeTests
{
    [Theory]
    [InlineData("idle")]
    [InlineData("partial")]
    [InlineData("completed")]
    public async Task Shutdown_cancels_pending_accepts_and_reads_without_racing_socket_disposal(string phase)
    {
        for (var iteration = 0; iteration < 32; iteration++)
        {
            var server = new RawCorsServer(optionsGrant: true, actualGrant: true);
            using var connection = new TcpClient();
            try
            {
                if (phase == "partial")
                {
                    await connection.ConnectAsync(server.BaseAddress.Host, server.BaseAddress.Port);
                    await server.ConnectionAccepted.WaitAsync(TimeSpan.FromSeconds(5));
                    await connection.GetStream().WriteAsync(Encoding.ASCII.GetBytes("GET / HTTP/1.1\r\n"));
                }
                if (phase == "completed")
                {
                    using var client = server.CreateClient();
                    using var response = await client.GetAsync("/probe");
                    response.EnsureSuccessStatusCode();
                    Assert.Single(server.Requests);
                }
            }
            finally
            {
                await server.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            }
        }
    }

    [Fact]
    public async Task Genuine_server_failure_propagates_and_still_closes_the_listener()
    {
        var expected = new InvalidOperationException("Synthetic server failure.");
        var server = new RawCorsServer(optionsGrant: true, actualGrant: true, onAccepted: () => throw expected);
        using var connection = new TcpClient();
        await connection.ConnectAsync(server.BaseAddress.Host, server.BaseAddress.Port);
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => server.Completion.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Same(expected, failure);
        var disposalFailure = await Assert.ThrowsAsync<InvalidOperationException>(() => server.DisposeAsync().AsTask());
        Assert.Same(expected, disposalFailure);
        using var probe = new TcpClient();
        await Assert.ThrowsAsync<SocketException>(() => probe.ConnectAsync(server.BaseAddress.Host, server.BaseAddress.Port).WaitAsync(TimeSpan.FromSeconds(5)));
    }
}
