using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Perron.Mcp.Tests;

public sealed class StdioTransportTests
{
    [Fact]
    public async Task Stdio_ListsPing_AndAnswersPong()
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
        Assert.Equal("ping", Assert.Single(tools).Name);

        var result = await client.CallToolAsync("ping", cancellationToken: TestContext.Current.CancellationToken);
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content));
        Assert.Equal("pong", text.Text);
    }
}
