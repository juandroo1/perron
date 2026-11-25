using System.Net;

using Microsoft.AspNetCore.Mvc.Testing;

using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Perron.Mcp.Tests;

public sealed class HttpTransportTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private const string LatestRevision = "2026-07-28";
    private const string InitializeHandshakeRevision = "2025-11-25";

    [Theory]
    [InlineData(null, LatestRevision)] // default client: server/discover, per-request metadata
    [InlineData(InitializeHandshakeRevision, InitializeHandshakeRevision)] // initialize handshake
    public async Task Connect_NegotiatesProtocol_AndReportsServerInfo(string? requested, string expected)
    {
        await using var client = await ConnectAsync(requested);

        Assert.Equal(expected, client.NegotiatedProtocolVersion);
        var serverInfo = client.ServerInfo;
        Assert.NotNull(serverInfo);
        Assert.Equal(McpServerSetup.ServerName, serverInfo.Name);
        Assert.False(string.IsNullOrWhiteSpace(serverInfo.Version));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(InitializeHandshakeRevision)]
    public async Task ListTools_ReturnsOnlyPing_MarkedReadOnly(string? requested)
    {
        await using var client = await ConnectAsync(requested);

        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        var tool = Assert.Single(tools);
        Assert.Equal("ping", tool.Name);
        var annotations = tool.ProtocolTool.Annotations;
        Assert.NotNull(annotations);
        Assert.True(annotations.ReadOnlyHint);
        Assert.False(annotations.DestructiveHint);
    }

    [Fact]
    public async Task CallPing_ReturnsPong()
    {
        await using var client = await ConnectAsync(null);

        var result = await client.CallToolAsync("ping", cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotEqual(true, result.IsError);
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content));
        Assert.Equal("pong", text.Text);
    }

    [Fact]
    public async Task Health_ReturnsHealthy()
    {
        using var http = factory.CreateClient();

        var response = await http.GetAsync("/health", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    private async Task<McpClient> ConnectAsync(string? protocolVersion)
    {
        var http = factory.CreateClient();
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = new Uri(http.BaseAddress!, "/mcp"),
                TransportMode = HttpTransportMode.StreamableHttp,
            },
            http,
            null,
            true);

        return await McpClient.CreateAsync(
            transport,
            new McpClientOptions { ProtocolVersion = protocolVersion },
            cancellationToken: TestContext.Current.CancellationToken);
    }
}
