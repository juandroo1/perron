using System.Web;

using ModelContextProtocol;

using Perron.Mcp.Tools;
using Perron.Mcp.Variants;

namespace Perron.Mcp.Tests;

/// <summary>Tool cut <c>many</c>: each tool asks the API like its standard counterpart and names its own tools in hints.</summary>
public sealed class ManyToolsTests
{
    private static readonly PerronVariant Many = PerronVariant.Default with { Tools = ToolCut.Many };
    private static readonly FixedClock RecordedAt = new(new DateTimeOffset(2026, 11, 25, 17, 12, 0, TimeSpan.FromHours(1)));

    [Fact]
    public async Task SearchStopsByName_GivesTheSameResultAsFindStops_WithItsOwnHint()
    {
        using var many = new TransportFixture(FixtureHandler.Always("locations-buchs-station.json"));
        using var standard = new TransportFixture(FixtureHandler.Always("locations-buchs-station.json"));

        var result = await ManyTools.SearchStopsByNameAsync(many.Client, Many, "Buchs", cancellationToken: Token);
        var expected = await FindStopsTool.FindStopsAsync(standard.Client, PerronVariant.Default, "Buchs", cancellationToken: Token);

        Assert.Equal(expected.Replace("use its ID in plan_trip", "use its ID in the connection tools", StringComparison.Ordinal), result);
        Assert.Contains("type=station", Assert.Single(many.Handler.Requests).Query);
    }

    [Fact]
    public async Task SearchStopsNear_SearchesByCoordinate()
    {
        using var transport = new TransportFixture(FixtureHandler.Always("locations-coordinates-station.json"));

        var result = await ManyTools.SearchStopsNearAsync(transport.Client, Many, 47.378177, 8.540192, 2, Token);

        Assert.StartsWith("Stops near 47.37818, 8.54019", result);
        var query = HttpUtility.ParseQueryString(Assert.Single(transport.Handler.Requests).Query);
        Assert.Equal("47.378177", query["x"]);
        Assert.Equal("8.540192", query["y"]);
    }

    [Fact]
    public async Task GetConnectionsDeparting_AsksForDepartures()
    {
        using var transport = new TransportFixture(FixtureHandler.Always("connections-zurich-bern-tomorrow-0800.json"));

        var result = await ManyTools.GetConnectionsDepartingAsync(transport.Client, Many, "8503000", "8507000", "2026-11-26T08:00", [TransportMode.Train], 10, 4, Token);

        Assert.Contains("leaving after Thu 26.11.2026 08:00, transfers of at least 10 min", result);
        var query = Query(transport);
        Assert.Null(query["isArrivalTime"]);
        Assert.Null(query["via"]);
        Assert.Equal("train", query["transportations[]"]);
        Assert.Equal("10", query["minTransferTime"]);
    }

    [Fact]
    public async Task GetConnectionsArriving_AsksForArrivals_AndNeedsATime()
    {
        using var transport = new TransportFixture(FixtureHandler.Always("connections-zurich-bern-arrive-0900.json"));

        var result = await ManyTools.GetConnectionsArrivingAsync(transport.Client, Many, "8503000", "8507000", "2026-11-26T09:00", cancellationToken: Token);
        var error = await Assert.ThrowsAsync<McpException>(
            () => ManyTools.GetConnectionsArrivingAsync(transport.Client, Many, "8503000", "8507000", " ", cancellationToken: Token));

        Assert.Contains("arriving by Thu 26.11.2026 09:00", result);
        Assert.Equal("1", Query(transport)["isArrivalTime"]);
        Assert.StartsWith("Give datetime, the latest arrival", error.Message);
    }

    [Fact]
    public async Task GetConnectionsVia_PassesTheVias_AndNeedsAtLeastOne()
    {
        using var transport = new TransportFixture(FixtureHandler.Always("connections-bern-zurich-via-olten.json"));

        var result = await ManyTools.GetConnectionsViaAsync(transport.Client, Many, "8507000", "8503000", ["8500218"], "2026-11-26T08:00", cancellationToken: Token);
        var error = await Assert.ThrowsAsync<McpException>(
            () => ManyTools.GetConnectionsViaAsync(transport.Client, Many, "8507000", "8503000", [], cancellationToken: Token));

        Assert.Contains("via Olten (8500218)", result);
        Assert.Equal("8500218", Query(transport)["via"]);
        Assert.Contains("use get_connections_departing or get_connections_arriving", error.Message);
    }

    [Fact]
    public async Task ConnectionTools_NameThemselves_AfterAnAmbiguousName()
    {
        using var transport = new TransportFixture(new FixtureHandler(uri => uri.AbsolutePath.EndsWith("/locations", StringComparison.Ordinal)
            ? "locations-buchs-station.json"
            : "connections-zurich-bern-now.json"));

        var result = await ManyTools.GetConnectionsArrivingAsync(transport.Client, Many, "Buchs", "8503000", "2026-11-26T09:00", cancellationToken: Token);

        Assert.Contains("Ask which one is meant, then call get_connections_arriving again with its ID.", result);
    }

    [Fact]
    public async Task UnknownStop_PointsToSearchStopsByName()
    {
        using var transport = new TransportFixture(FixtureHandler.Always("locations-unknown.json"));

        var error = await Assert.ThrowsAsync<McpException>(
            () => ManyTools.GetDeparturesAsync(transport.Client, RecordedAt, Many, "Xyzzyplatz", cancellationToken: Token));

        Assert.Contains("search with search_stops_by_name", error.Message);
    }

    [Theory]
    [InlineData(false, "departure", "Departures at Bern (8507000)")]
    [InlineData(true, "arrival", "Arrivals at Bern (8507000)")]
    public async Task DeparturesAndArrivals_AskForTheirBoard(bool arrivals, string type, string header)
    {
        using var transport = new TransportFixture(FixtureHandler.Always(arrivals ? "stationboard-bern-arrivals.json" : "stationboard-bern-departures.json"));

        var result = arrivals
            ? await ManyTools.GetArrivalsAsync(transport.Client, RecordedAt, Many, "8507000", cancellationToken: Token)
            : await ManyTools.GetDeparturesAsync(transport.Client, RecordedAt, Many, "8507000", cancellationToken: Token);

        Assert.StartsWith(header, result);
        Assert.Equal(type, Query(transport)["type"] ?? "departure");
    }

    [Fact]
    public async Task SearchStopsByName_NeedsAName()
    {
        using var transport = new TransportFixture(FixtureHandler.Always("locations-buchs-station.json"));

        var error = await Assert.ThrowsAsync<McpException>(() => ManyTools.SearchStopsByNameAsync(transport.Client, Many, "  ", cancellationToken: Token));

        Assert.Equal("Give a stop or place name, e.g. \"Bern\".", error.Message);
        Assert.Empty(transport.Handler.Requests);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static System.Collections.Specialized.NameValueCollection Query(TransportFixture transport) =>
        HttpUtility.ParseQueryString(transport.Handler.Requests.Last().Query);
}
