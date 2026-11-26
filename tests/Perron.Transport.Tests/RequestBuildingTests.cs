using System.Globalization;

namespace Perron.Transport.Tests;

public class RequestBuildingTests
{
    [Fact]
    public void Locations_ByName_IsEscaped()
    {
        var url = TransportApiClient.BuildLocationsUrl(LocationQuery.ByName(" Zürich HB "));

        Assert.Equal("locations?query=Z%C3%BCrich%20HB", url);
    }

    [Fact]
    public void Locations_ByCoordinate_UsesInvariantDecimalPoint_AndStationType()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("de-CH");
        try
        {
            var url = TransportApiClient.BuildLocationsUrl(
                LocationQuery.ByCoordinate(new Coordinate(47.378177, 8.540192)) with { Type = LocationType.Station });

            Assert.Equal("locations?x=47.378177&y=8.540192&type=station", url);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Locations_WithoutNameOrCoordinate_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => TransportApiClient.BuildLocationsUrl(LocationQuery.ByName("  ")));
    }

    [Theory]
    [InlineData("2026-11-26T07:00:00Z", "2026-11-26", "08:00")] // winter time, UTC+1
    [InlineData("2027-03-31T06:00:00Z", "2027-03-31", "08:00")] // summer time, UTC+2
    [InlineData("2026-11-26T23:30:00Z", "2026-11-27", "00:30")] // date changes in Zurich
    public void Connections_At_IsSentAsZurichLocalTime(string utc, string date, string time)
    {
        var url = TransportApiClient.BuildConnectionsUrl(
            new ConnectionQuery("Zürich HB", "Bern") { At = DateTimeOffset.Parse(utc, CultureInfo.InvariantCulture) });

        Assert.Contains($"&date={date}&time={Uri.EscapeDataString(time)}", url);
    }

    [Fact]
    public void Connections_AllOptions_InStableOrder()
    {
        var url = TransportApiClient.BuildConnectionsUrl(new ConnectionQuery("Bern", "Zürich HB")
        {
            Via = ["Olten"],
            At = new DateTimeOffset(2026, 11, 26, 8, 0, 0, TimeSpan.FromHours(1)),
            IsArrivalTime = true,
            TransportTypes = [TransportType.Train, TransportType.Bus, TransportType.Train],
            Limit = 4,
            Page = -1,
        });

        Assert.Equal(
            "connections?from=Bern&to=Z%C3%BCrich%20HB&via=Olten&date=2026-11-26&time=08%3A00&isArrivalTime=1"
            + "&transportations%5B%5D=train&transportations%5B%5D=bus&limit=4&page=-1",
            url);
    }

    [Fact]
    public void Connections_TwoVias_UseArrayNotation()
    {
        var url = TransportApiClient.BuildConnectionsUrl(new ConnectionQuery("Lausanne", "St. Gallen") { Via = ["Bern", "Zürich HB"] });

        Assert.Contains("&via%5B%5D=Bern&via%5B%5D=Z%C3%BCrich%20HB", url);
    }

    [Fact]
    public void Connections_InvalidInput_IsRejectedBeforeAnyRequest()
    {
        Assert.Throws<ArgumentException>(() => TransportApiClient.BuildConnectionsUrl(new ConnectionQuery("A", "B") { Via = ["X", "Y", "Z"] }));
        Assert.Throws<ArgumentException>(() => TransportApiClient.BuildConnectionsUrl(new ConnectionQuery(" ", "B")));
        Assert.Throws<ArgumentOutOfRangeException>(() => TransportApiClient.BuildConnectionsUrl(new ConnectionQuery("A", "B") { Limit = 17 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => TransportApiClient.BuildConnectionsUrl(new ConnectionQuery("A", "B") { Page = 11 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => TransportApiClient.BuildConnectionsUrl(new ConnectionQuery("A", "B") { MinTransferTime = 61 }));
    }

    [Fact]
    public void Connections_MinTransferTime_IsSent()
    {
        var url = TransportApiClient.BuildConnectionsUrl(new ConnectionQuery("Basel SBB", "Lugano") { MinTransferTime = 10 });

        Assert.EndsWith("&minTransferTime=10", url);
    }

    [Fact]
    public void Stationboard_ByIdWithArrivalsAndDatetime()
    {
        var url = TransportApiClient.BuildStationboardUrl(new StationboardQuery
        {
            Station = "Bern",
            StationId = "8507000",
            At = new DateTimeOffset(2026, 11, 26, 7, 0, 0, TimeSpan.Zero),
            Type = StationboardType.Arrival,
            TransportTypes = [TransportType.Tram],
            Limit = 10,
        });

        // The ID takes precedence over the name.
        Assert.Equal(
            "stationboard?id=8507000&limit=10&transportations%5B%5D=tram&datetime=2026-11-26%2008%3A00&type=arrival",
            url);
    }

    [Fact]
    public void Stationboard_InvalidInput_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => TransportApiClient.BuildStationboardUrl(new StationboardQuery()));
        Assert.Throws<ArgumentOutOfRangeException>(() => TransportApiClient.BuildStationboardUrl(new StationboardQuery { Station = "Bern", Limit = 0 }));
    }
}
