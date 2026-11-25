using System.Net;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

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
        await using var client = await ConnectAsync(factory, requested);

        Assert.Equal(expected, client.NegotiatedProtocolVersion);
        var serverInfo = client.ServerInfo;
        Assert.NotNull(serverInfo);
        Assert.Equal(McpServerSetup.ServerName, serverInfo.Name);
        Assert.False(string.IsNullOrWhiteSpace(serverInfo.Version));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(InitializeHandshakeRevision)]
    public async Task ListTools_ReturnsFindStops_MarkedReadOnly(string? requested)
    {
        await using var client = await ConnectAsync(factory, requested);

        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        var tool = Assert.Single(tools);
        Assert.Equal("find_stops", tool.Name);
        var annotations = tool.ProtocolTool.Annotations;
        Assert.NotNull(annotations);
        Assert.True(annotations.ReadOnlyHint);
        Assert.True(annotations.IdempotentHint);
        Assert.False(annotations.DestructiveHint);
        Assert.True(annotations.OpenWorldHint);

        var schema = tool.ProtocolTool.InputSchema;
        Assert.Equal(
            ["latitude", "limit", "longitude", "query"],
            schema.GetProperty("properties").EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.True(
            !schema.TryGetProperty("required", out var required) || required.GetArrayLength() == 0,
            "find_stops must not have required parameters");
    }

    [Fact]
    public async Task CallFindStops_ReturnsCandidates_FromTheTransportApi()
    {
        var handler = FixtureHandler.Always("locations-buchs-station.json");
        await using var app = WithTransportHandler(handler);
        await using var client = await ConnectAsync(app, null);

        var result = await client.CallToolAsync(
            "find_stops",
            new Dictionary<string, object?> { ["query"] = "Buchs", ["limit"] = 2 },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotEqual(true, result.IsError);
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.Contains("8502123 | Buchs AG | train", text);
        Assert.Contains("8509404 | Buchs SG | train", text);
        Assert.Contains("Several stops match \"Buchs\"", text);
        Assert.Contains("type=station", Assert.Single(handler.Requests).Query);
    }

    [Fact]
    public async Task CallFindStops_WithoutArguments_IsAToolError_WithAHint()
    {
        var handler = FixtureHandler.Always("locations-buchs-station.json");
        await using var app = WithTransportHandler(handler);
        await using var client = await ConnectAsync(app, null);

        var result = await client.CallToolAsync("find_stops", cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.Contains("Give a query", text);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Health_ReturnsHealthy()
    {
        using var http = factory.CreateClient();

        var response = await http.GetAsync("/health", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    private WebApplicationFactory<Program> WithTransportHandler(FixtureHandler handler) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.ConfigureHttpClientDefaults(http => http.ConfigurePrimaryHttpMessageHandler(() => handler))));

    private static async Task<McpClient> ConnectAsync(WebApplicationFactory<Program> app, string? protocolVersion)
    {
        var http = app.CreateClient();
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
