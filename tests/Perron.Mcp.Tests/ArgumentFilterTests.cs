using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

using Perron.Mcp.Tools;

namespace Perron.Mcp.Tests;

/// <summary>Argument mistakes are recoverable (AK-4.1): numbers for string parameters are accepted, others get a hint.</summary>
public sealed class ArgumentFilterTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task StopIdsAsNumbers_AreAccepted()
    {
        var handler = new FixtureHandler(_ => "connections-lausanne-stgallen-via-two.json");
        await using var client = await ConnectAsync(handler);

        var result = await CallAsync(client, "plan_trip", new()
        {
            ["from"] = 8501120,
            ["to"] = 8506302,
            ["via"] = new object[] { 8507000, "8503000" },
            ["datetime"] = "2026-11-26T08:00",
        });

        Assert.NotEqual(true, result.IsError);
        Assert.StartsWith("Connections Lausanne (8501120) → St. Gallen (8506302)", Text(result));
        var query = System.Web.HttpUtility.ParseQueryString(Assert.Single(handler.Requests).Query);
        Assert.Equal("8501120", query["from"]);
        Assert.Equal("8507000,8503000", query["via[]"]);
    }

    [Fact]
    public async Task StopIdAsNumber_WorksForDepartures()
    {
        var handler = new FixtureHandler(_ => "stationboard-by-id.json");
        await using var client = await ConnectAsync(handler);

        var result = await CallAsync(client, "get_departures", new() { ["stop"] = 8507000, ["limit"] = 5 });

        Assert.NotEqual(true, result.IsError);
        Assert.StartsWith("Departures at Bern (8507000)", Text(result));
    }

    [Theory]
    [InlineData("time_is", "depart")]
    [InlineData("limit", "three")]
    [InlineData("transport_types", "train")]
    public async Task OtherWrongArguments_AreAToolError_WithAHint(string name, string value)
    {
        var handler = new FixtureHandler(_ => "connections-zurich-bern-now.json");
        await using var client = await ConnectAsync(handler);

        var result = await CallAsync(client, "plan_trip", new() { ["from"] = "8503000", ["to"] = "8507000", [name] = value });

        Assert.True(result.IsError);
        Assert.Equal(ArgumentFilter.InvalidArgumentsMessage, Text(result));
        Assert.Empty(handler.Requests);
    }

    private static string Text(CallToolResult result) => Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;

    private static Task<CallToolResult> CallAsync(McpClient client, string tool, Dictionary<string, object?> arguments) =>
        client.CallToolAsync(tool, arguments, cancellationToken: TestContext.Current.CancellationToken).AsTask();

    private async Task<McpClient> ConnectAsync(FixtureHandler handler)
    {
        var app = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.ConfigureHttpClientDefaults(http => http.ConfigurePrimaryHttpMessageHandler(() => handler))));
        var http = app.CreateClient();
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions { Endpoint = new Uri(http.BaseAddress!, "/mcp"), TransportMode = HttpTransportMode.StreamableHttp },
            http,
            null,
            true);
        return await McpClient.CreateAsync(transport, cancellationToken: TestContext.Current.CancellationToken);
    }
}
