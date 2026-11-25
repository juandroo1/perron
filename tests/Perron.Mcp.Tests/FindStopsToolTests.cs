using ModelContextProtocol;

using Perron.Mcp.Tools;
using Perron.Transport;

namespace Perron.Mcp.Tests;

public sealed class FindStopsToolTests
{
    [Fact]
    public async Task ExactName_ListsStops_WithoutAmbiguityHint()
    {
        using var fixture = new TransportFixture(FixtureHandler.Always("locations-zurich-hb.json"));

        var result = await FindAsync(fixture, query: "Zürich HB", limit: 3);

        Assert.Equal(
            """
            Stops for "Zürich HB" (ID | name | kind | latitude, longitude):
            8503000 | Zürich HB | train | 47.37818, 8.54021
            8591442 | Zürich, Zoo | tram | 47.38157, 8.57155
            8591367 | Zürich, Sihlpost/HB | tram | 47.37610, 8.53420
            Fahrplandaten: opentransportdata.swiss
            """.ReplaceLineEndings("\n"),
            result);
    }

    [Fact]
    public async Task AmbiguousName_ListsCandidates_AndAsksWhichOneIsMeant()
    {
        using var fixture = new TransportFixture(FixtureHandler.Always("locations-buchs-station.json"));

        var result = await FindAsync(fixture, query: "Buchs");

        Assert.Equal(
            """
            Stops for "Buchs" (ID | name | kind | latitude, longitude):
            8502123 | Buchs AG | train | 47.38974, 8.06958
            8509404 | Buchs SG | train | 47.16841, 9.47864
            8574041 | Buchs SG, Bahnhof | bus | 47.16834, 9.47812
            8503527 | Buchs-Dällikon | train | 47.45341, 8.43560
            8590212 | Buchs AG, Heuweg | bus | 47.38912, 8.08787
            Several stops match "Buchs". Ask which one is meant, or use its ID in plan_trip.
            Fahrplandaten: opentransportdata.swiss
            """.ReplaceLineEndings("\n"),
            result);
    }

    [Fact]
    public async Task Coordinate_ListsNearestStops_WithDistance()
    {
        using var fixture = new TransportFixture(FixtureHandler.Always("locations-coordinates-station.json"));

        var result = await FindAsync(fixture, latitude: 47.378177, longitude: 8.540192);

        Assert.Equal(
            """
            Stops near 47.37818, 8.54019 (ID | name | kind | latitude, longitude | distance):
            8503000 | Zürich HB | train | 47.37818, 8.54021 | 1 m
            8587348 | Zürich, Bahnhofplatz/HB | tram | 47.37724, 8.53934 | 122 m
            8587349 | Zürich, Bahnhofquai/HB | tram | 47.37756, 8.54174 | 135 m
            8503446 | Zürich Landesmuseum (See) | - | 47.37905, 8.54158 | 142 m
            8591067 | Zürich, Bahnhofstrasse/HB | tram | 47.37622, 8.53946 | 224 m
            Fahrplandaten: opentransportdata.swiss
            """.ReplaceLineEndings("\n"),
            result);
    }

    [Fact]
    public async Task UnknownName_IsANormalResult_WithAHint()
    {
        using var fixture = new TransportFixture(FixtureHandler.Always("locations-unknown.json"));

        var result = await FindAsync(fixture, query: "Xyzzyplatz");

        Assert.Equal(
            """
            No stop found for "Xyzzyplatz". Check the spelling, or try a shorter name or the name of the town.
            Fahrplandaten: opentransportdata.swiss
            """.ReplaceLineEndings("\n"),
            result);
    }

    [Fact]
    public async Task NameSearch_AsksForStationsOnly()
    {
        using var fixture = new TransportFixture(FixtureHandler.Always("locations-buchs-station.json"));

        await FindAsync(fixture, query: "  Buchs ");

        var request = Assert.Single(fixture.Handler.Requests);
        Assert.Equal("/v1/locations", request.AbsolutePath);
        Assert.Contains("query=Buchs", request.Query);
        Assert.Contains("type=station", request.Query);
    }

    [Fact]
    public async Task CoordinateSearch_AsksForStationsOnly_LatitudeAsX()
    {
        using var fixture = new TransportFixture(FixtureHandler.Always("locations-coordinates-station.json"));

        await FindAsync(fixture, latitude: 47.378177, longitude: 8.540192);

        var request = Assert.Single(fixture.Handler.Requests);
        Assert.Contains("x=47.378177", request.Query);
        Assert.Contains("y=8.540192", request.Query);
        Assert.Contains("type=station", request.Query);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    public async Task Limit_CapsTheNumberOfStops(int limit)
    {
        using var fixture = new TransportFixture(FixtureHandler.Always("locations-buchs-station.json"));

        var result = await FindAsync(fixture, query: "Buchs", limit: limit);

        Assert.Equal(limit, result.Split('\n').Count(line => line.StartsWith("85", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData(null, null, null, 5, "Give a query")]
    [InlineData("   ", null, null, 5, "Give a query")]
    [InlineData("Bern", 46.9, 7.4, 5, "not both")]
    [InlineData(null, 46.9, null, 5, "Give both latitude and longitude")]
    [InlineData(null, null, 7.4, 5, "Give both latitude and longitude")]
    [InlineData(null, 91.0, 7.4, 5, "between -90 and 90")]
    [InlineData(null, 46.9, -181.0, 5, "between -180 and 180")]
    [InlineData(null, double.NaN, 7.4, 5, "between -90 and 90")]
    [InlineData("Bern", null, null, 0, "limit must be between 1 and 10")]
    [InlineData("Bern", null, null, 11, "limit must be between 1 and 10")]
    public async Task InvalidInput_IsAToolError_WithAHint_AndCallsNoApi(
        string? query, double? latitude, double? longitude, int limit, string hint)
    {
        using var fixture = new TransportFixture(FixtureHandler.Always("locations-buchs-station.json"));

        var error = await Assert.ThrowsAsync<McpException>(() => FindAsync(fixture, query, latitude, longitude, limit));

        Assert.Contains(hint, error.Message);
        Assert.Empty(fixture.Handler.Requests);
    }

    private static Task<string> FindAsync(
        TransportFixture fixture,
        string? query = null,
        double? latitude = null,
        double? longitude = null,
        int limit = FindStopsTool.DefaultLimit) =>
        FindStopsTool.FindStopsAsync(
            fixture.Client, query, latitude, longitude, limit, TestContext.Current.CancellationToken);
}
