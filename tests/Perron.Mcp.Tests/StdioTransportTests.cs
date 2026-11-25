using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Perron.Mcp.Tests;

public sealed class StdioTransportTests
{
    [Fact]
    public async Task Stdio_ListsFindStops_AndAnswersToolCalls()
    {
        // The referenced Perron.Mcp build output is copied next to the test assembly.
        var serverDll = typeof(McpServerSetup).Assembly.Location;
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "perron-stdio",
            Command = "dotnet",
            Arguments = [serverDll, "--stdio"],
        });

        await using var client = await McpClient.CreateAsync(
            transport,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(client.ServerInfo);
        Assert.Equal(McpServerSetup.ServerName, client.ServerInfo.Name);

        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("find_stops", Assert.Single(tools).Name);

        // Without arguments the tool answers with a validation error before any API call: no network in tests.
        var result = await client.CallToolAsync("find_stops", cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.IsError);
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content));
        Assert.Contains("Give a query", text.Text);
    }
}
