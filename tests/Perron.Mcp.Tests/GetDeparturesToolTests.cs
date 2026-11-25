using System.Web;

using ModelContextProtocol;

using Perron.Mcp.Tools;
using Perron.Mcp.Variants;
using Perron.Transport;

namespace Perron.Mcp.Tests;

public sealed class GetDeparturesToolTests
{
    /// <summary>When the "now" fixtures were recorded (tests/fixtures/README.md).</summary>
    private static readonly FixedClock RecordedAt = new(new DateTimeOffset(2026, 11, 25, 17, 12, 0, TimeSpan.FromHours(1)));

    /// <summary>Snapshot cases: recorded /stationboard response, tool arguments as the fixture's request.</summary>
    public static TheoryData<string, string, string?, BoardType, TransportMode[]?, int> Cases() => new()
    {
        { "bern-departures", "Bern", null, BoardType.Departure, null, 10 },
        { "bern-arrivals", "Bern", null, BoardType.Arrival, null, 10 },
        { "zurich-central-tram", "Zürich, Central", null, BoardType.Departure, [TransportMode.Tram], 10 },
        { "bern-tomorrow-0800", "8507000", "2026-11-26T08:00", BoardType.Departure, null, 10 },
        { "by-id", "8507000", null, BoardType.Departure, null, 5 },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Result_MatchesSnapshot(
        string fixture, string stop, string? datetime, BoardType type, TransportMode[]? types, int limit)
    {
        using var transport = new TransportFixture(Router($"stationboard-{fixture}.json"));

        var result = await GetAsync(transport, stop, datetime, type, types, limit);

        Snapshot.Match($"get_departures-{fixture}", result);
    }

    [Fact]
    public async Task Request_UsesTheResolvedId_AndAllOptions()
    {
        using var transport = new TransportFixture(Router("stationboard-bern-tomorrow-0800.json"));

        await GetAsync(transport, "Bern", "2026-11-26T08:00", BoardType.Arrival, [TransportMode.Train, TransportMode.Bus], 30);

        var request = Assert.Single(transport.Handler.Requests, uri => uri.AbsolutePath.EndsWith("/stationboard", StringComparison.Ordinal));
        var query = HttpUtility.ParseQueryString(request.Query);
        Assert.Equal("8507000", query["id"]);
        Assert.Null(query["station"]);
        Assert.Equal("2026-11-26 08:00", query["datetime"]);
        Assert.Equal("arrival", query["type"]);
        Assert.Equal("train,bus", query["transportations[]"]);
        Assert.Equal("30", query["limit"]);
    }

    [Fact]
    public async Task SoftLimit_IsNotCut()
    {
        using var transport = new TransportFixture(Router("stationboard-zurich-central-tram.json"));

        var result = await GetAsync(transport, "Zürich, Central", types: [TransportMode.Tram], limit: 10);

        // The API answered limit=10 with 14 trams: the last ones leave in the same minute.
        Assert.Equal(14, result.Split('\n').Count(line => char.IsDigit(line.FirstOrDefault())));
    }

    [Fact]
    public async Task AmbiguousName_ReturnsCandidates_InsteadOfDepartures()
    {
        using var transport = new TransportFixture(Router("stationboard-bern-departures.json"));

        var result = await GetAsync(transport, "Buchs");

        Snapshot.Match("get_departures-ambiguous", result);
        Assert.DoesNotContain(transport.Handler.Requests, uri => uri.AbsolutePath.EndsWith("/stationboard", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UnknownName_IsAToolError()
    {
        using var transport = new TransportFixture(Router("stationboard-bern-departures.json"));

        var error = await Assert.ThrowsAsync<McpException>(() => GetAsync(transport, "Xyzzyplatz"));

        Assert.StartsWith("No stop found for \"Xyzzyplatz\" (stop)", error.Message);
    }

    [Fact]
    public async Task UnknownId_IsAToolError()
    {
        using var transport = new TransportFixture(Router("stationboard-unknown.json"));

        var error = await Assert.ThrowsAsync<McpException>(() => GetAsync(transport, "8599999"));

        Assert.Equal("No stop with ID \"8599999\". Search with find_stops and use the stop's ID.", error.Message);
    }

    [Theory]
    [InlineData("", null, 10, "Give a stop")]
    [InlineData("8507000", "gestern", 10, "is not a date and time")]
    [InlineData("8507000", null, 0, "limit must be between 1 and 30")]
    [InlineData("8507000", null, 31, "limit must be between 1 and 30")]
    public async Task InvalidInput_IsAToolError_WithAHint_AndCallsNoApi(string stop, string? datetime, int limit, string hint)
    {
        using var transport = new TransportFixture(Router("stationboard-bern-departures.json"));

        var error = await Assert.ThrowsAsync<McpException>(() => GetAsync(transport, stop, datetime, limit: limit));

        Assert.Contains(hint, error.Message);
        Assert.Empty(transport.Handler.Requests);
    }

    /// <summary>Delays, platform changes, entries after midnight and vehicles ending at the stop.</summary>
    [Fact]
    public async Task Format_ShowsDelays_PlatformChanges_NextDay_AndVehiclesEndingHere()
    {
        var bern = new Location("8507000", "Bern", null, null, "train");
        static DateTimeOffset At(int day, int hour, int minute) => new(2026, 11, day, hour, minute, 0, TimeSpan.FromHours(1));
        var board = new Stationboard(
            bern,
            [
                new StationboardEntry(new Checkpoint(bern, null, At(26, 23, 58), 4, "6", new Prognosis(null, null, "7")), "IC", "8", "SBB", "Brig"),
                new StationboardEntry(new Checkpoint(bern, null, At(27, 0, 3), 0, "22", null), "S", "9", "BLS", "Bern"),
            ]);
        var transport = new BoardOnlyClient(board);

        var arrivals = await GetDeparturesTool.GetDeparturesAsync(
            transport, RecordedAt, PerronVariant.Default, "8507000", "2026-11-26T23:55", BoardType.Arrival, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(
            """
            Arrivals at Bern (8507000) from Thu 26.11.2026 23:55:
            23:58 (+4 min) IC 8 continues to Brig pl. 6 (now 7)
            Fri 27.11. 00:03 S 9 ends here pl. 22
            Fahrplandaten: opentransportdata.swiss
            """.ReplaceLineEndings("\n"),
            arrivals);
    }

    [Fact]
    public async Task EmptyBoard_IsANormalResult_WithAHint()
    {
        var transport = new BoardOnlyClient(new Stationboard(new Location("8507000", "Bern", null, null, "train"), []));

        var result = await GetDeparturesTool.GetDeparturesAsync(
            transport, RecordedAt, PerronVariant.Default, "8507000", "2026-11-26T03:00", transport_types: [TransportMode.Ship], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(
            """
            No departures at Bern (8507000) from Thu 26.11.2026 03:00. Try a later time or other means of transport.
            Fahrplandaten: opentransportdata.swiss
            """.ReplaceLineEndings("\n"),
            result);
    }

    private static FixtureHandler Router(string stationboard) => new(uri =>
    {
        if (!uri.AbsolutePath.EndsWith("/locations", StringComparison.Ordinal))
        {
            return stationboard;
        }

        return HttpUtility.ParseQueryString(uri.Query)["query"] switch
        {
            "Bern" => "locations-bern-station.json",
            "Zürich, Central" => "locations-zurich-central-station.json",
            "Buchs" => "locations-buchs-station.json",
            _ => "locations-unknown.json",
        };
    });

    private static Task<string> GetAsync(
        TransportFixture transport,
        string stop,
        string? datetime = null,
        BoardType type = BoardType.Departure,
        TransportMode[]? types = null,
        int limit = GetDeparturesTool.DefaultLimit) =>
        GetDeparturesTool.GetDeparturesAsync(
            transport.Client, RecordedAt, PerronVariant.Default, stop, datetime, type, types, limit, TestContext.Current.CancellationToken);

    /// <summary>Answers stationboard queries with a fixed board; stops are given as IDs, so no search happens.</summary>
    private sealed class BoardOnlyClient(Stationboard board) : ITransportApiClient
    {
        public Task<IReadOnlyList<Location>> SearchLocationsAsync(LocationQuery query, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("No stop search expected.");

        public Task<ConnectionsResult> GetConnectionsAsync(ConnectionQuery query, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("No connection search expected.");

        public Task<Stationboard> GetStationboardAsync(StationboardQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(board);
    }
}
