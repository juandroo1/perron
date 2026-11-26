using Microsoft.Extensions.Logging.Abstractions;

using Perron.Bench.Checks;
using Perron.Bench.Registry;

namespace Perron.Bench.Tests;

public sealed class McpServerCheckTests(PerronMcpFactory server) : IClassFixture<PerronMcpFactory>
{
    [Fact]
    public async Task Run_AgainstPerronOverHttp_ReturnsServerInfoProtocolAndTools()
    {
        var check = new McpServerCheck(new McpServerConnector(new DelegateHttpClientFactory(server.CreateClient), NullLoggerFactory.Instance));
        var definition = new ServerDefinition(
            "perron-rich", ServerDefinition.Http, "http://localhost/mcp", null, null, null, null, "repository",
            false, false, null, null, [new CapabilityDefinition("stops", "find_stops")]);

        var result = await check.RunAsync(definition, null, new Dictionary<string, string?>(), TestContext.Current.CancellationToken);

        Assert.Equal("perron-rich", result.ServerId);
        Assert.Equal("perron-rich", result.ServerName);
        Assert.False(string.IsNullOrWhiteSpace(result.ServerVersion));
        Assert.Equal("2026-07-28", result.ProtocolVersion);
        Assert.Equal(["find_stops", "get_departures", "plan_trip"], result.ToolNames);
        Assert.All(result.Tools, tool => Assert.False(string.IsNullOrWhiteSpace(tool.Description)));
    }
}
