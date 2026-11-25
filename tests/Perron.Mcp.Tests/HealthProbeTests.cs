using Microsoft.AspNetCore.Mvc.Testing;

namespace Perron.Mcp.Tests;

public sealed class HealthProbeTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Probe_HealthyServer_ReturnsZero()
    {
        using var http = factory.CreateClient();

        var exitCode = await HealthProbe.RunAsync(
            http, new Uri(http.BaseAddress!, "/health"), TestContext.Current.CancellationToken);

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public async Task Probe_ErrorStatus_ReturnsOne()
    {
        using var http = factory.CreateClient();

        var exitCode = await HealthProbe.RunAsync(
            http, new Uri(http.BaseAddress!, "/does-not-exist"), TestContext.Current.CancellationToken);

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task Probe_Unreachable_ReturnsOne()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };

        // Port 1 on loopback: nothing listens there, the connection is refused.
        var exitCode = await HealthProbe.RunAsync(
            http, new Uri("http://127.0.0.1:1/health"), TestContext.Current.CancellationToken);

        Assert.Equal(1, exitCode);
    }
}
