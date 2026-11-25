using System.Net;
using System.Text.Json;

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
    public async Task ListTools_ReturnsTheTools_MarkedReadOnly(string? requested)
    {
        await using var client = await ConnectAsync(factory, requested);

        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["find_stops", "get_departures", "plan_trip"], tools.Select(tool => tool.Name).Order(StringComparer.Ordinal));
        foreach (var tool in tools)
        {
            var annotations = tool.ProtocolTool.Annotations;
            Assert.NotNull(annotations);
            Assert.True(annotations.ReadOnlyHint);
            Assert.True(annotations.IdempotentHint);
            Assert.False(annotations.DestructiveHint);
            Assert.True(annotations.OpenWorldHint);
        }

        var findStops = tools.Single(tool => tool.Name == "find_stops").ProtocolTool.InputSchema;
        Assert.Equal(["latitude", "limit", "longitude", "query"], PropertyNames(findStops));
        Assert.Empty(Required(findStops));
    }

    [Fact]
    public async Task PlanTripSchema_HasSnakeCaseParameters_AndStringEnums()
    {
        await using var client = await ConnectAsync(factory, null);

        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        var schema = tools.Single(tool => tool.Name == "plan_trip").ProtocolTool.InputSchema;
        Assert.Equal(["datetime", "from", "limit", "time_is", "to", "transport_types", "via"], PropertyNames(schema));
        Assert.Equal(["from", "to"], Required(schema).Order(StringComparer.Ordinal));

        var properties = schema.GetProperty("properties");
        Assert.Equal(["departure", "arrival"], EnumValues(properties.GetProperty("time_is")));
        Assert.Equal(
            ["train", "tram", "bus", "ship", "cableway"],
            EnumValues(properties.GetProperty("transport_types").GetProperty("items")));
    }

    [Fact]
    public async Task GetDeparturesSchema_HidesInjectedServices_AndHasStringEnums()
    {
        await using var client = await ConnectAsync(factory, null);

        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        var schema = tools.Single(tool => tool.Name == "get_departures").ProtocolTool.InputSchema;
        Assert.Equal(["datetime", "limit", "stop", "transport_types", "type"], PropertyNames(schema));
        Assert.Equal(["stop"], Required(schema));
        Assert.Equal(["departure", "arrival"], EnumValues(schema.GetProperty("properties").GetProperty("type")));
    }

    [Fact]
    public async Task CallGetDepartures_ReturnsTheBoard()
    {
        var handler = new FixtureHandler(uri => uri.AbsolutePath.EndsWith("/locations", StringComparison.Ordinal)
            ? "locations-bern-station.json"
            : "stationboard-bern-arrivals.json");
        await using var app = WithTransportHandler(handler);
        await using var client = await ConnectAsync(app, null);

        var result = await client.CallToolAsync(
            "get_departures",
            new Dictionary<string, object?> { ["stop"] = "Bern", ["type"] = "arrival", ["limit"] = 10 },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotEqual(true, result.IsError);
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.StartsWith("Arrivals at Bern (8507000) from now (", text);
        Assert.Equal("arrival", System.Web.HttpUtility.ParseQueryString(handler.Requests.Last().Query)["type"]);
    }

    [Fact]
    public async Task CallPlanTrip_AcceptsStringEnums_AndReturnsConnections()
    {
        var handler = new FixtureHandler(uri => uri.AbsolutePath.EndsWith("/locations", StringComparison.Ordinal)
            ? "locations-zurich-hb.json"
            : "connections-zurich-airport-train.json");
        await using var app = WithTransportHandler(handler);
        await using var client = await ConnectAsync(app, null);

        var result = await client.CallToolAsync(
            "plan_trip",
            new Dictionary<string, object?>
            {
                ["from"] = "Zürich HB",
                ["to"] = "8503016",
                ["datetime"] = "2026-11-26T08:00",
                ["time_is"] = "departure",
                ["transport_types"] = new[] { "train" },
                ["limit"] = 3,
            },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotEqual(true, result.IsError);
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.StartsWith("Connections Zürich HB (8503000) → ", text);
        Assert.Equal("train", System.Web.HttpUtility.ParseQueryString(handler.Requests.Last().Query)["transportations[]"]);
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

    private static IEnumerable<string> PropertyNames(JsonElement schema) =>
        schema.GetProperty("properties").EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal);

    private static IEnumerable<string> Required(JsonElement schema) =>
        schema.TryGetProperty("required", out var required) ? required.EnumerateArray().Select(r => r.GetString()!) : [];

    private static IEnumerable<string> EnumValues(JsonElement property) =>
        property.GetProperty("enum").EnumerateArray().Select(value => value.GetString()!);

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
