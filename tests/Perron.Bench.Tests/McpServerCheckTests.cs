using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Perron.Bench.Checks;

namespace Perron.Bench.Tests;

public sealed class McpServerCheckTests(PerronMcpFactory server) : IClassFixture<PerronMcpFactory>
{
    private static readonly Uri Endpoint = new("http://localhost/mcp");

    [Fact]
    public async Task Run_AgainstPerron_ReturnsServerInfoProtocolAndTools()
    {
        var check = new McpServerCheck(new DelegateHttpClientFactory(server.CreateClient), NullLoggerFactory.Instance);

        var result = await check.RunAsync("perron", Endpoint, TestContext.Current.CancellationToken);

        Assert.Equal("perron", result.ServerId);
        Assert.Equal("perron", result.ServerName);
        Assert.False(string.IsNullOrWhiteSpace(result.ServerVersion));
        Assert.Equal("2026-07-28", result.ProtocolVersion);
        Assert.Equal(["find_stops", "plan_trip"], result.ToolNames);
    }

    [Fact]
    public async Task CheckCommand_UnreachableServer_FailsWithExitCodeOne()
    {
        // No resilience pipeline here, so the refused connection fails fast.
        var check = new McpServerCheck(
            new DelegateHttpClientFactory(() => new HttpClient { Timeout = TimeSpan.FromSeconds(5) }),
            NullLoggerFactory.Instance);
        var command = new CheckCommand(
            check, Options.Create(new PerronEndpointOptions { McpUrl = "http://127.0.0.1:1/mcp" }));
        var output = new StringWriter();

        var exitCode = await command.RunAsync(["perron"], [], output, TestContext.Current.CancellationToken);

        Assert.Equal(ExitCodes.CheckFailed, exitCode);
        Assert.Contains("FAIL  not reachable at http://127.0.0.1:1/mcp", output.ToString());
    }
}
