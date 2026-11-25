namespace Perron.Transport.Tests;

/// <summary>Parsing of recorded real responses (tests/fixtures/transport-opendata).</summary>
public class ResponseParsingTests
{
    private static readonly TimeSpan WinterOffset = TimeSpan.FromHours(1);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Locations_AmbiguousName_ReturnsCandidatesWithIdsIconsAndCoordinates()
    {
        using var fixture = new ClientFixture(FakeHandler.Fixture("locations-buchs.json"));

        var locations = await fixture.Client.SearchLocationsAsync(LocationQuery.ByName("Buchs"), Ct);

        var aargau = Assert.Single(locations, l => l.Name == "Buchs AG");
        Assert.Equal("8502123", aargau.Id);
        Assert.Equal("train", aargau.Icon);
        Assert.Equal(new Coordinate(47.389739, 8.069584), aargau.Coordinate);
        Assert.Contains(locations, l => l.Name == "Buchs SG");
        Assert.Contains(locations, l => l.Name == "Buchs-Dällikon");
    }

    [Fact]
    public async Task Locations_ByCoordinate_HaveDistances_AndAddressesHaveNoId()
    {
        using var all = new ClientFixture(FakeHandler.Fixture("locations-coordinates.json"));
        var nearby = await all.Client.SearchLocationsAsync(LocationQuery.ByCoordinate(new Coordinate(47.378177, 8.540192)), Ct);

        var address = nearby[0];
        Assert.Null(address.Id);
        Assert.Equal("Bahnhofplatz 15, Zürich", address.Name);

        using var stations = new ClientFixture(FakeHandler.Fixture("locations-coordinates-station.json"));
        var stops = await stations.Client.SearchLocationsAsync(
            LocationQuery.ByCoordinate(new Coordinate(47.378177, 8.540192)) with { Type = LocationType.Station }, Ct);

        Assert.Equal("Zürich HB", stops[0].Name);
        Assert.Equal(1, stops[0].DistanceMeters);
    }

    [Fact]
    public async Task Connections_MapTimesPlatformsDelaysAndSections_InZurichTime()
    {
        using var fixture = new ClientFixture(FakeHandler.Fixture("connections-zurich-bern-now.json"));

        var result = await fixture.Client.GetConnectionsAsync(new ConnectionQuery("Zürich HB", "Bern"), Ct);

        var connection = result.Connections[0];
        Assert.Equal(new DateTimeOffset(2026, 11, 25, 17, 32, 0, WinterOffset), connection.From.Departure);
        Assert.Equal(WinterOffset, connection.From.Departure!.Value.Offset);
        Assert.Equal(new DateTimeOffset(2026, 11, 25, 18, 28, 0, WinterOffset), connection.To.Arrival);
        Assert.Equal("32", connection.From.Platform);
        Assert.Equal("6", connection.To.Platform);
        Assert.Equal(0, connection.From.DelayMinutes);
        Assert.Equal(TimeSpan.FromMinutes(56), connection.Duration);
        Assert.Equal(0, connection.Transfers);
        Assert.Equal(["IC 1"], connection.Products);

        var journey = Assert.Single(connection.Sections).Journey;
        Assert.NotNull(journey);
        Assert.Equal("IC 1", journey.Line);
        Assert.Equal("SBB", journey.Operator);
        Assert.Equal("Genève-Aéroport", journey.Destination);

        Assert.Equal("Zürich HB", result.ResolvedFrom?.Name);
        Assert.Equal("Bern", result.ResolvedTo?.Name);
    }

    [Fact]
    public async Task Connections_WithTransfers_HaveRealTimeDelaysPerSection()
    {
        using var fixture = new ClientFixture(FakeHandler.Fixture("connections-zurich-bern-now.json"));

        var connection = (await fixture.Client.GetConnectionsAsync(new ConnectionQuery("Zürich HB", "Bern"), Ct)).Connections[1];

        Assert.Equal(2, connection.Transfers);
        Assert.Equal(3, connection.Sections.Count);
        Assert.Equal("3CD", connection.Sections[1].Arrival.Platform);
        Assert.Equal(1, connection.Sections[1].Departure.DelayMinutes);
        Assert.Null(connection.Sections[2].Departure.DelayMinutes); // no real-time data
    }

    [Fact]
    public async Task Connections_AmbiguousOrigin_ExposeTheSilentlyChosenStation_AndWalks()
    {
        using var fixture = new ClientFixture(FakeHandler.Fixture("connections-buchs-ambiguous.json"));

        var result = await fixture.Client.GetConnectionsAsync(new ConnectionQuery("Buchs", "Zürich HB"), Ct);

        Assert.Equal("Buchs AG", result.ResolvedFrom?.Name);
        var walk = result.Connections[1].Sections.Select(s => s.Walk).OfType<Walk>().Single();
        Assert.Equal(TimeSpan.FromMinutes(4), walk.Duration);
    }

    [Fact]
    public async Task Connections_TrainNumbers_LoseLeadingZeros()
    {
        using var fixture = new ClientFixture(FakeHandler.Fixture("connections-basel-lugano-transfer10.json"));

        var connection = (await fixture.Client.GetConnectionsAsync(new ConnectionQuery("Basel SBB", "Lugano"), Ct)).Connections[0];

        Assert.Equal("EC 15", connection.Sections[1].Journey?.Line);
    }

    [Fact]
    public async Task Connections_TwoVias_ResolveTheRealDestination()
    {
        using var fixture = new ClientFixture(FakeHandler.Fixture("connections-lausanne-stgallen-via-two.json"));

        var result = await fixture.Client.GetConnectionsAsync(
            new ConnectionQuery("Lausanne", "St. Gallen") { Via = ["Bern", "Zürich HB"] }, Ct);

        // The API's top-level "to" says Bern here.
        Assert.Equal("St. Gallen", result.ResolvedTo?.Name);
    }

    [Fact]
    public async Task Stationboard_MapsLineDestinationPlatformDelayAndPrognosis()
    {
        using var fixture = new ClientFixture(FakeHandler.Fixture("stationboard-bern-departures.json"));

        var board = await fixture.Client.GetStationboardAsync(new StationboardQuery { Station = "Bern" }, Ct);

        Assert.Equal("Bern", board.Station?.Name);
        Assert.Equal("8507000", board.Station?.Id);
        var first = board.Entries[0];
        Assert.Equal("S 1", first.Line);
        Assert.Equal("Fribourg/Freiburg", first.Destination);
        Assert.Equal("1E-H", first.Stop.Platform);
        Assert.Equal(1, first.Stop.DelayMinutes);
        Assert.Equal(new DateTimeOffset(2026, 11, 25, 17, 15, 0, WinterOffset), first.Stop.Departure);
        Assert.Equal(new DateTimeOffset(2026, 11, 25, 17, 16, 0, WinterOffset), first.Stop.Prognosis?.Departure);
        Assert.Equal(new DateTimeOffset(2026, 11, 25, 17, 14, 31, WinterOffset), first.Stop.Prognosis?.Arrival);
    }

    [Fact]
    public async Task Stationboard_OperatorPaddingIsRemoved()
    {
        using var fixture = new ClientFixture(FakeHandler.Fixture("stationboard-zurich-central-tram.json"));

        var board = await fixture.Client.GetStationboardAsync(new StationboardQuery { Station = "Zürich, Central" }, Ct);

        Assert.Equal("T 3", board.Entries[0].Line);
        Assert.Equal("VBZ F", board.Entries[0].Operator);
    }

    [Fact]
    public async Task UnknownNames_GiveEmptyResults_NotErrors()
    {
        using var locations = new ClientFixture(FakeHandler.Fixture("locations-unknown.json"));
        Assert.Empty(await locations.Client.SearchLocationsAsync(LocationQuery.ByName("Xyzzyplatz"), Ct));

        using var connections = new ClientFixture(FakeHandler.Fixture("connections-unknown-from.json"));
        var result = await connections.Client.GetConnectionsAsync(new ConnectionQuery("Xyzzyplatz", "Bern"), Ct);
        Assert.Empty(result.Connections);
        Assert.Null(result.ResolvedFrom);

        using var stationboard = new ClientFixture(FakeHandler.Fixture("stationboard-unknown.json"));
        var board = await stationboard.Client.GetStationboardAsync(new StationboardQuery { Station = "Xyzzyplatz" }, Ct);
        Assert.Null(board.Station);
        Assert.Empty(board.Entries);
    }

    [Theory]
    [InlineData("2026-11-25T17:32:00+0100", 2026, 11, 25, 17, 32, 0, 1)]
    [InlineData("2026-11-25T17:14:31+01:00", 2026, 11, 25, 17, 14, 31, 1)]
    [InlineData("2026-11-25T16:32:00+0000", 2026, 11, 25, 17, 32, 0, 1)] // converted into Zurich
    [InlineData("2027-07-01T08:00:00+0200", 2027, 7, 1, 8, 0, 0, 2)]
    public void ApiTimes_AreParsedIntoZurich(string value, int y, int mo, int d, int h, int mi, int s, int offsetHours)
    {
        var parsed = SwissTime.ParseApiTime(value);

        Assert.Equal(new DateTimeOffset(y, mo, d, h, mi, s, TimeSpan.FromHours(offsetHours)), parsed);
        Assert.Equal(TimeSpan.FromHours(offsetHours), parsed.Offset);
    }

    [Theory]
    [InlineData("00d00:56:00", 0, 0, 56)]
    [InlineData("00d03:21:00", 0, 3, 21)]
    [InlineData("01d02:05:00", 1, 2, 5)]
    public void Durations_AreParsed(string value, int days, int hours, int minutes)
    {
        Assert.Equal(new TimeSpan(days, hours, minutes, 0), ApiDuration.Parse(value));
    }

    [Fact]
    public void UnexpectedDuration_IsAFormatError()
    {
        Assert.Throws<FormatException>(() => ApiDuration.Parse("56 min"));
    }
}
